CREATE SCHEMA IF NOT EXISTS recipes;

CREATE TABLE recipes.recipes (
    id              UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    household_id    UUID        NOT NULL,
    title           TEXT        NOT NULL,
    description     TEXT,
    servings        INTEGER,
    source_url      TEXT,
    notes           TEXT,
    created_by      UUID        NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE recipes.ingredients (
    id          UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    recipe_id   UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
    name        TEXT    NOT NULL,
    quantity    TEXT,
    unit        TEXT,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE recipes.instructions (
    id          UUID    NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    recipe_id   UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
    step_order  INTEGER NOT NULL,
    text        TEXT    NOT NULL
);

CREATE INDEX ix_recipes_household    ON recipes.recipes (household_id);
CREATE INDEX ix_ingredients_recipe   ON recipes.ingredients (recipe_id);
CREATE INDEX ix_instructions_recipe  ON recipes.instructions (recipe_id);
