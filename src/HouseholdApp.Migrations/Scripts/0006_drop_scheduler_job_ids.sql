-- Recurring templates are scheduled in Quartz under keys derived from the template id
-- (job group = recurring job name, job name = template id), so the scheduler-side id
-- they used to carry is no longer stored.
ALTER TABLE tasks.recurring_tasks DROP COLUMN IF EXISTS scheduler_job_id;
ALTER TABLE expenses.recurring_expenses DROP COLUMN IF EXISTS scheduler_job_id;
