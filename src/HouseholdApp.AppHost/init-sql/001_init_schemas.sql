\connect householdapp

-- identity
CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE identity.users (
    id            UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    subject       TEXT        NOT NULL UNIQUE,
    email         TEXT        NOT NULL,
    display_name  TEXT        NOT NULL,
    picture_url   TEXT        NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_users_email ON identity.users (email);

-- households
CREATE SCHEMA IF NOT EXISTS households;

CREATE TABLE households.households (
    id         UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name       TEXT        NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE households.members (
    household_id UUID     NOT NULL REFERENCES households.households(id),
    user_id      UUID     NOT NULL,
    role         SMALLINT NOT NULL,
    PRIMARY KEY (household_id, user_id)
);

CREATE TABLE households.invitations (
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

CREATE INDEX ix_members_user_id ON households.members (user_id);

-- lists
CREATE SCHEMA IF NOT EXISTS lists;

CREATE TABLE lists.lists (
    id           UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    household_id UUID        NOT NULL,
    name         TEXT        NOT NULL,
    created_by   UUID        NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE lists.items (
    id           UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    list_id      UUID        NOT NULL REFERENCES lists.lists(id) ON DELETE CASCADE,
    name         TEXT        NOT NULL,
    category     TEXT,
    sort_order   INTEGER     NOT NULL DEFAULT 1000,
    is_completed BOOLEAN     NOT NULL DEFAULT false,
    completed_by UUID,
    completed_at TIMESTAMPTZ
);

CREATE INDEX ix_lists_items_list_id   ON lists.items (list_id);
CREATE INDEX ix_lists_lists_household ON lists.lists (household_id);

-- tasks
CREATE SCHEMA IF NOT EXISTS tasks;

CREATE TABLE tasks.recurring_tasks (
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

CREATE TABLE tasks.tasks (
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

CREATE INDEX ix_recurring_tasks_household ON tasks.recurring_tasks (household_id);
CREATE INDEX ix_tasks_household           ON tasks.tasks (household_id);
CREATE INDEX ix_tasks_recurring           ON tasks.tasks (recurring_task_id);

-- recipes
CREATE SCHEMA IF NOT EXISTS recipes;

CREATE TABLE recipes.recipes (
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

CREATE TABLE recipes.ingredients (
    id         UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    recipe_id  UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
    name       TEXT    NOT NULL,
    quantity   TEXT,
    unit       TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE recipes.instructions (
    id         UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    recipe_id  UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
    step_order INTEGER NOT NULL,
    text       TEXT    NOT NULL
);

CREATE INDEX ix_recipes_household   ON recipes.recipes (household_id);
CREATE INDEX ix_ingredients_recipe  ON recipes.ingredients (recipe_id);
CREATE INDEX ix_instructions_recipe ON recipes.instructions (recipe_id);

-- expenses (Marten manages the schema; this table is Dapper-managed)
CREATE SCHEMA IF NOT EXISTS expenses;

CREATE TABLE expenses.recurring_expenses (
    id               UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    household_id     UUID    NOT NULL,
    expense_group_id UUID    NOT NULL,
    description      TEXT    NOT NULL,
    cron_expression  TEXT    NOT NULL,
    is_active        BOOLEAN NOT NULL DEFAULT true,
    scheduler_job_id UUID,
    funding_sources  JSONB   NOT NULL DEFAULT '[]'::jsonb,
    allocations      JSONB   NOT NULL DEFAULT '[]'::jsonb
);

CREATE INDEX ix_recurring_expenses_household ON expenses.recurring_expenses (household_id);

-- authz (Valtuutus)
CREATE SCHEMA IF NOT EXISTS authz;

CREATE TABLE authz.transactions (
    "id"         char(26)    NOT NULL,
    "created_at" timestamptz NOT NULL,
    PRIMARY KEY ("id")
);

CREATE INDEX authz_idx_created_at ON authz.transactions USING btree (created_at);

CREATE TABLE authz.attributes (
    "id"           bigint                 NOT NULL GENERATED ALWAYS AS IDENTITY,
    "entity_type"  character varying(256) NOT NULL,
    "entity_id"    character varying(64)  NOT NULL,
    "attribute"    character varying(64)  NOT NULL,
    "value"        jsonb                  NOT NULL,
    created_tx_id  char(26)               NOT NULL,
    deleted_tx_id  char(26),
    PRIMARY KEY ("id")
);

CREATE INDEX        authz_idx_attributes        ON authz.attributes ("entity_type", "entity_id", "attribute") INCLUDE ("value");
CREATE UNIQUE INDEX authz_unique_attributes     ON authz.attributes ("entity_type", "entity_id", "attribute") WHERE deleted_tx_id IS NULL;

CREATE TABLE authz.relation_tuples (
    "id"               bigint                 NOT NULL GENERATED ALWAYS AS IDENTITY,
    "entity_type"      character varying(256) NOT NULL,
    "entity_id"        character varying(64)  NOT NULL,
    "relation"         character varying(64)  NOT NULL,
    "subject_type"     character varying(256) NOT NULL,
    "subject_id"       character varying(64)  NOT NULL,
    "subject_relation" character varying(64)  NOT NULL,
    created_tx_id      char(26)               NOT NULL,
    deleted_tx_id      char(26),
    PRIMARY KEY ("id")
);

CREATE INDEX authz_idx_tuples_entity_relation  ON authz.relation_tuples ("entity_type", "relation");
CREATE INDEX authz_idx_tuples_subject_entities ON authz.relation_tuples ("entity_type", "relation", "subject_type", "subject_id") INCLUDE ("entity_id", "subject_relation");
CREATE INDEX authz_idx_tuples_user             ON authz.relation_tuples ("entity_type", "entity_id", "relation", "subject_id");
CREATE INDEX authz_idx_tuples_userset          ON authz.relation_tuples ("entity_type", "entity_id", "relation", "subject_type", "subject_relation");
CREATE INDEX authz_idx_tuples_direct           ON authz.relation_tuples ("entity_type", "entity_id", "relation", "subject_id")
    INCLUDE ("subject_type", "created_tx_id", "deleted_tx_id")
    WHERE subject_relation = '';
CREATE INDEX authz_idx_tuples_indirect         ON authz.relation_tuples ("entity_type", "entity_id", "relation")
    INCLUDE ("subject_type", "subject_id", "subject_relation", "created_tx_id", "deleted_tx_id")
    WHERE subject_relation <> '';
