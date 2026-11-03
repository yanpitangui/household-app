-- Recurring expenses live in Marten streams; every row of this table became one (same id) when #24/#25 shipped.
DROP TABLE IF EXISTS expenses.recurring_expenses;
