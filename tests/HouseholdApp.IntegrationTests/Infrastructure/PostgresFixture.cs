using Dapper;
using HouseholdApp.Application.Shared.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace HouseholdApp.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = default!;

    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        var builder = new NpgsqlDataSourceBuilder(ConnectionString);
        DataSource = builder.Build();

        PersistenceExtensions.RegisterTypeHandlers();

        await using var conn = await DataSource.OpenConnectionAsync();
        await ApplySchemasAsync(conn);
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static async Task ApplySchemasAsync(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync("""
            CREATE SCHEMA IF NOT EXISTS identity;
            CREATE TABLE IF NOT EXISTS identity.users (
                id            UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                subject       TEXT        NOT NULL UNIQUE,
                email         TEXT        NOT NULL,
                display_name  TEXT        NOT NULL,
                picture_url   TEXT        NULL,
                created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                last_login_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE SCHEMA IF NOT EXISTS households;
            CREATE TABLE IF NOT EXISTS households.households (
                id         UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                name       TEXT        NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS households.members (
                household_id UUID     NOT NULL REFERENCES households.households(id),
                user_id      UUID     NOT NULL,
                role         SMALLINT NOT NULL,
                PRIMARY KEY (household_id, user_id)
            );
            CREATE TABLE IF NOT EXISTS households.invitations (
                id           UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                household_id UUID        NOT NULL REFERENCES households.households(id),
                token        TEXT        NOT NULL UNIQUE,
                created_by   UUID        NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                expires_at   TIMESTAMPTZ NOT NULL,
                status       SMALLINT    NOT NULL DEFAULT 0,
                accepted_by  UUID,
                accepted_at  TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_members_user_id ON households.members (user_id);

            CREATE SCHEMA IF NOT EXISTS lists;
            CREATE TABLE IF NOT EXISTS lists.lists (
                id           UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                household_id UUID        NOT NULL,
                name         TEXT        NOT NULL,
                created_by   UUID        NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS lists.items (
                id           UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                list_id      UUID        NOT NULL REFERENCES lists.lists(id) ON DELETE CASCADE,
                name         TEXT        NOT NULL,
                category     TEXT,
                sort_order   INTEGER     NOT NULL DEFAULT 1000,
                is_completed BOOLEAN     NOT NULL DEFAULT false,
                completed_by UUID,
                completed_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_lists_items_list_id   ON lists.items (list_id);
            CREATE INDEX IF NOT EXISTS ix_lists_lists_household ON lists.lists (household_id);

            CREATE SCHEMA IF NOT EXISTS tasks;
            CREATE TABLE IF NOT EXISTS tasks.recurring_tasks (
                id                  UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                household_id        UUID        NOT NULL,
                title               TEXT        NOT NULL,
                description         TEXT,
                default_assigned_to UUID,
                cron_expression     TEXT        NOT NULL,
                is_active           BOOLEAN     NOT NULL DEFAULT true,
                next_run_at         TIMESTAMPTZ,
                scheduler_job_id    UUID
            );
            CREATE TABLE IF NOT EXISTS tasks.tasks (
                id                UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                household_id      UUID        NOT NULL,
                title             TEXT        NOT NULL,
                description       TEXT,
                assigned_to       UUID,
                due_date          TIMESTAMPTZ,
                status            SMALLINT    NOT NULL DEFAULT 0,
                recurring_task_id UUID        REFERENCES tasks.recurring_tasks(id),
                created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
                completed_at      TIMESTAMPTZ,
                completed_by      UUID
            );
            CREATE INDEX IF NOT EXISTS ix_recurring_tasks_household ON tasks.recurring_tasks (household_id);
            CREATE INDEX IF NOT EXISTS ix_tasks_household           ON tasks.tasks (household_id);
            CREATE INDEX IF NOT EXISTS ix_tasks_recurring           ON tasks.tasks (recurring_task_id);

            CREATE SCHEMA IF NOT EXISTS recipes;
            CREATE TABLE IF NOT EXISTS recipes.recipes (
                id           UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                household_id UUID        NOT NULL,
                title        TEXT        NOT NULL,
                description  TEXT,
                servings     INTEGER,
                source_url   TEXT,
                notes        TEXT,
                created_by   UUID        NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS recipes.ingredients (
                id         UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                recipe_id  UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
                name       TEXT    NOT NULL,
                quantity   TEXT,
                unit       TEXT,
                sort_order INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS recipes.instructions (
                id         UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                recipe_id  UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
                step_order INTEGER NOT NULL,
                text       TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_recipes_household   ON recipes.recipes (household_id);
            CREATE INDEX IF NOT EXISTS ix_ingredients_recipe  ON recipes.ingredients (recipe_id);
            CREATE INDEX IF NOT EXISTS ix_instructions_recipe ON recipes.instructions (recipe_id);

            CREATE SCHEMA IF NOT EXISTS expenses;
            CREATE TABLE IF NOT EXISTS expenses.recurring_expenses (
                id                   UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                household_id         UUID        NOT NULL,
                expense_group_id     UUID        NOT NULL,
                description          TEXT        NOT NULL,
                recurrence_frequency TEXT        NOT NULL,
                start_at             TIMESTAMPTZ NOT NULL,
                cron_expression      TEXT        NOT NULL,
                is_active            BOOLEAN     NOT NULL DEFAULT true,
                scheduler_job_id     UUID,
                funding_sources      JSONB       NOT NULL DEFAULT '[]'::jsonb,
                allocations          JSONB       NOT NULL DEFAULT '[]'::jsonb
            );
            CREATE INDEX IF NOT EXISTS ix_recurring_expenses_household ON expenses.recurring_expenses (household_id);

            CREATE SCHEMA IF NOT EXISTS authz;
            CREATE TABLE IF NOT EXISTS authz.transactions ("id" char(26) NOT NULL, "created_at" timestamptz NOT NULL, PRIMARY KEY ("id"));
            CREATE INDEX IF NOT EXISTS authz_idx_created_at ON authz.transactions USING btree (created_at);
            CREATE TABLE IF NOT EXISTS authz.attributes ("id" bigint NOT NULL GENERATED ALWAYS AS IDENTITY, "entity_type" character varying(256) NOT NULL, "entity_id" character varying(64) NOT NULL, "attribute" character varying(64) NOT NULL, "value" jsonb NOT NULL, created_tx_id char(26) NOT NULL, deleted_tx_id char(26), PRIMARY KEY ("id"));
            CREATE INDEX IF NOT EXISTS authz_idx_attributes ON authz.attributes ("entity_type", "entity_id", "attribute") INCLUDE ("value");
            CREATE UNIQUE INDEX IF NOT EXISTS authz_unique_attributes ON authz.attributes ("entity_type", "entity_id", "attribute") WHERE deleted_tx_id IS NULL;
            CREATE TABLE IF NOT EXISTS authz.relation_tuples ("id" bigint NOT NULL GENERATED ALWAYS AS IDENTITY, "entity_type" character varying(256) NOT NULL, "entity_id" character varying(64) NOT NULL, "relation" character varying(64) NOT NULL, "subject_type" character varying(256) NOT NULL, "subject_id" character varying(64) NOT NULL, "subject_relation" character varying(64) NOT NULL, created_tx_id char(26) NOT NULL, deleted_tx_id char(26), PRIMARY KEY ("id"));
            CREATE INDEX IF NOT EXISTS authz_idx_tuples_entity_relation  ON authz.relation_tuples ("entity_type", "relation");
            CREATE INDEX IF NOT EXISTS authz_idx_tuples_subject_entities ON authz.relation_tuples ("entity_type", "relation", "subject_type", "subject_id") INCLUDE ("entity_id", "subject_relation");
            CREATE INDEX IF NOT EXISTS authz_idx_tuples_user             ON authz.relation_tuples ("entity_type", "entity_id", "relation", "subject_id");
            CREATE INDEX IF NOT EXISTS authz_idx_tuples_userset          ON authz.relation_tuples ("entity_type", "entity_id", "relation", "subject_type", "subject_relation");
            CREATE INDEX IF NOT EXISTS authz_idx_tuples_direct           ON authz.relation_tuples ("entity_type", "entity_id", "relation", "subject_id") INCLUDE ("subject_type", "created_tx_id", "deleted_tx_id") WHERE subject_relation = '';
            CREATE INDEX IF NOT EXISTS authz_idx_tuples_indirect         ON authz.relation_tuples ("entity_type", "entity_id", "relation") INCLUDE ("subject_type", "subject_id", "subject_relation", "created_tx_id", "deleted_tx_id") WHERE subject_relation <> '';
            """);
    }
}
