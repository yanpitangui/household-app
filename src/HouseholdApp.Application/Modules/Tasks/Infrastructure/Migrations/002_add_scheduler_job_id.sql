ALTER TABLE tasks.recurring_tasks
    ADD COLUMN IF NOT EXISTS scheduler_job_id UUID;
