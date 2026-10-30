-- Recurring tasks move from a typed cron to a schedule (frequency, first date, time in UTC). next_run_at stays as the
-- sweep's cursor. Only crons the frequency picker can express convert; anything else stops the deploy rather than
-- silently changing when a task fires.
CREATE FUNCTION tasks.cron_to_schedule(cron TEXT, OUT frequency TEXT, OUT first_date DATE, OUT run_time TIME)
LANGUAGE plpgsql AS $$
DECLARE
    f TEXT[] := regexp_split_to_array(trim(cron), '\s+');
    today DATE := (now() AT TIME ZONE 'UTC')::date;
    dom INT;
    mon INT;
    dow INT;
    candidate_year INT;
BEGIN
    IF array_length(f, 1) <> 5 OR f[1] !~ '^\d+$' OR f[2] !~ '^\d+$' THEN
        RAISE EXCEPTION 'Recurring task cron "%" has no picker equivalent', cron;
    END IF;
    run_time := make_time(f[2]::int, f[1]::int, 0);

    IF f[3] = '*' AND f[4] = '*' AND f[5] = '*' THEN
        frequency := 'Daily';
        first_date := today;
    ELSIF f[3] = '*' AND f[4] = '*' AND f[5] ~ '^[0-7]$' THEN
        frequency := 'Weekly';
        dow := f[5]::int % 7;
        first_date := today + ((dow - extract(dow FROM today)::int + 7) % 7);
    ELSIF f[3] ~ '^\d+$' AND f[4] = '*' AND f[5] = '*' THEN
        frequency := 'Monthly';
        dom := f[3]::int;
        IF dom NOT BETWEEN 1 AND 31 THEN
            RAISE EXCEPTION 'Recurring task cron "%" has no picker equivalent', cron;
        END IF;
        first_date := make_date(extract(year FROM today)::int, extract(month FROM today)::int, 1);
        -- The first month with this day on or after today, so the day of month the task kept is what the schedule keeps.
        WHILE dom > extract(day FROM (first_date + interval '1 month' - interval '1 day'))::int
              OR first_date + (dom - 1) < today LOOP
            first_date := (first_date + interval '1 month')::date;
        END LOOP;
        first_date := first_date + (dom - 1);
    ELSIF f[3] ~ '^\d+$' AND f[4] ~ '^\d+$' AND f[5] = '*' THEN
        frequency := 'Annually';
        dom := f[3]::int;
        mon := f[4]::int;
        IF dom NOT BETWEEN 1 AND 31 OR mon NOT BETWEEN 1 AND 12 THEN
            RAISE EXCEPTION 'Recurring task cron "%" has no picker equivalent', cron;
        END IF;
        -- Year 2000 admits every calendar date that can ever occur, including 29 February.
        IF dom > extract(day FROM (make_date(2000, mon, 1) + interval '1 month' - interval '1 day'))::int THEN
            RAISE EXCEPTION 'Recurring task cron "%" has no picker equivalent', cron;
        END IF;
        candidate_year := extract(year FROM today)::int;
        first_date := make_date(candidate_year, mon, 1);
        WHILE dom > extract(day FROM (first_date + interval '1 month' - interval '1 day'))::int
              OR first_date + (dom - 1) < today LOOP
            candidate_year := candidate_year + 1;
            first_date := make_date(candidate_year, mon, 1);
        END LOOP;
        first_date := first_date + (dom - 1);
    ELSE
        RAISE EXCEPTION 'Recurring task cron "%" has no picker equivalent', cron;
    END IF;
END $$;

ALTER TABLE tasks.recurring_tasks
    ADD COLUMN frequency TEXT,
    ADD COLUMN first_date DATE,
    ADD COLUMN run_time TIME;

UPDATE tasks.recurring_tasks r
SET frequency = s.frequency, first_date = s.first_date, run_time = s.run_time
FROM (SELECT id, (tasks.cron_to_schedule(cron_expression)).* FROM tasks.recurring_tasks WHERE is_active) s
WHERE r.id = s.id;

-- Inactive templates never fire again; they keep a readable schedule without their cron having to convert.
UPDATE tasks.recurring_tasks
SET frequency = 'Monthly', first_date = (now() AT TIME ZONE 'UTC')::date, run_time = '09:00'
WHERE frequency IS NULL;

-- Strictly after now: today's run already fired under the old cron, so it must not fire again on deploy day.
UPDATE tasks.recurring_tasks
SET next_run_at = (CASE
        WHEN first_date + run_time > (now() AT TIME ZONE 'UTC') THEN first_date + run_time
        ELSE (first_date + CASE frequency
                WHEN 'Daily' THEN interval '1 day'
                WHEN 'Weekly' THEN interval '7 days'
                WHEN 'Monthly' THEN interval '1 month'
                ELSE interval '1 year'
            END)::date + run_time
    END) AT TIME ZONE 'UTC'
WHERE is_active;

ALTER TABLE tasks.recurring_tasks
    ALTER COLUMN frequency SET NOT NULL,
    ALTER COLUMN first_date SET NOT NULL,
    ALTER COLUMN run_time SET NOT NULL,
    DROP COLUMN cron_expression;

DROP FUNCTION tasks.cron_to_schedule;

CREATE INDEX ix_recurring_tasks_due ON tasks.recurring_tasks (next_run_at) WHERE is_active;
