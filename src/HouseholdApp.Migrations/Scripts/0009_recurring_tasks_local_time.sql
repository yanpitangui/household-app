-- Recurring task schedules were entered in UTC; they become wall-clock times in the household's zone, same instants.
-- next_run_at is an instant and stays.
UPDATE tasks.recurring_tasks r
SET first_date = ((r.first_date + r.run_time) AT TIME ZONE 'UTC' AT TIME ZONE h.time_zone)::date,
    run_time   = ((r.first_date + r.run_time) AT TIME ZONE 'UTC' AT TIME ZONE h.time_zone)::time
FROM households.households h
WHERE h.id = r.household_id;
