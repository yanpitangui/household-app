CREATE SCHEMA IF NOT EXISTS tasks;

CREATE TABLE tasks.recurring_tasks (
    id                  UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    household_id        UUID        NOT NULL,
    title               TEXT        NOT NULL,
    description         TEXT,
    default_assigned_to UUID,
    cron_expression     TEXT        NOT NULL,
    is_active           BOOLEAN     NOT NULL DEFAULT true,
    next_run_at         TIMESTAMPTZ
);

CREATE TABLE tasks.tasks (
    id                  UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    household_id        UUID        NOT NULL,
    title               TEXT        NOT NULL,
    description         TEXT,
    assigned_to         UUID,
    due_date            TIMESTAMPTZ,
    status              SMALLINT    NOT NULL DEFAULT 0,
    recurring_task_id   UUID        REFERENCES tasks.recurring_tasks(id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at        TIMESTAMPTZ,
    completed_by        UUID
);

CREATE INDEX ix_tasks_household ON tasks.tasks (household_id);
CREATE INDEX ix_tasks_recurring  ON tasks.tasks (recurring_task_id);
