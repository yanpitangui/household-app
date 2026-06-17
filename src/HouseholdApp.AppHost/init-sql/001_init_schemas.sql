\connect householdapp

CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- identity
CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE identity.users (
    id            UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
    id         UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
    id           UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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

-- catalog
CREATE SCHEMA IF NOT EXISTS catalog;

CREATE TABLE catalog.categories (
    id           UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    household_id UUID,
    language     TEXT,
    name         TEXT        NOT NULL,
    emoji        TEXT        NOT NULL,
    sort_order   INTEGER     NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uix_catalog_categories_global    ON catalog.categories (lower(name), language) WHERE household_id IS NULL;
CREATE UNIQUE INDEX uix_catalog_categories_household ON catalog.categories (household_id, lower(name)) WHERE household_id IS NOT NULL;
CREATE INDEX        ix_catalog_categories_household  ON catalog.categories (household_id);

CREATE TABLE catalog.items (
    id           UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    household_id UUID,
    language     TEXT,
    name         TEXT        NOT NULL,
    category_id  UUID        REFERENCES catalog.categories(id) ON DELETE SET NULL,
    default_unit TEXT,
    popularity   INTEGER     NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uix_catalog_items_global     ON catalog.items (lower(name), language)         WHERE household_id IS NULL;
CREATE UNIQUE INDEX uix_catalog_items_household  ON catalog.items (household_id, lower(name))     WHERE household_id IS NOT NULL;
CREATE INDEX        ix_catalog_items_household   ON catalog.items (household_id);
CREATE INDEX        ix_catalog_items_name_trgm   ON catalog.items USING gin (name gin_trgm_ops);

-- Seed: English categories
INSERT INTO catalog.categories (household_id, language, name, emoji, sort_order) VALUES
    (NULL, 'en', 'Fruits & Vegetables',      '🍅', 1),
    (NULL, 'en', 'Meat & Fish',              '🥩', 2),
    (NULL, 'en', 'Bread & Bakery',           '🍞', 3),
    (NULL, 'en', 'Dairy & Eggs',             '🥛', 4),
    (NULL, 'en', 'Frozen & Convenience',     '❄️',  5),
    (NULL, 'en', 'Grains & Cereals',         '🌾', 6),
    (NULL, 'en', 'Beverages',                '🥤', 7),
    (NULL, 'en', 'Ingredients & Condiments', '🧂', 8),
    (NULL, 'en', 'Sweets & Candy',           '🍫', 9),
    (NULL, 'en', 'Household & Garden',       '🪴', 10),
    (NULL, 'en', 'Cleaning & Hygiene',       '🧴', 11),
    (NULL, 'en', 'Pet Supplies',             '🐾', 12),
    (NULL, 'en', 'Prepared Food',            '🍱', 13),
    (NULL, 'en', 'Kitchen & Utensils',       '🍴', 14),
    (NULL, 'en', 'Chips & Snacks',           '🍿', 15),
    (NULL, 'en', 'Batteries & Electronics',  '🔋', 16);

-- Seed: pt-BR categories
INSERT INTO catalog.categories (household_id, language, name, emoji, sort_order) VALUES
    (NULL, 'pt-BR', 'Frutas & Legumes',         '🍅', 1),
    (NULL, 'pt-BR', 'Carne & Peixe',            '🥩', 2),
    (NULL, 'pt-BR', 'Pão & Confeitaria',         '🍞', 3),
    (NULL, 'pt-BR', 'Laticínios',               '🥛', 4),
    (NULL, 'pt-BR', 'Congelados & Conveniente',  '❄️',  5),
    (NULL, 'pt-BR', 'Cereais & Grãos',          '🌾', 6),
    (NULL, 'pt-BR', 'Bebidas',                   '🥤', 7),
    (NULL, 'pt-BR', 'Ingredientes & Condimentos','🧂', 8),
    (NULL, 'pt-BR', 'Lanches & Doces',           '🍫', 9),
    (NULL, 'pt-BR', 'Artesanato & Jardim',       '🪴', 10),
    (NULL, 'pt-BR', 'Limpeza & Higiene',         '🧴', 11),
    (NULL, 'pt-BR', 'Animais',                   '🐾', 12),
    (NULL, 'pt-BR', 'Comida Pronta',             '🍱', 13),
    (NULL, 'pt-BR', 'Utensílios',               '🍴', 14),
    (NULL, 'pt-BR', 'Petiscos',                  '🍿', 15),
    (NULL, 'pt-BR', 'Baterias & Eletrônicos',   '🔋', 16);

-- Seed: English global items
INSERT INTO catalog.items (household_id, language, name, category_id, default_unit) VALUES
    -- Fruits & Vegetables
    (NULL, 'en', 'Lettuce',        (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Tomato',         (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Onion',          (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Garlic',         (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Potato',         (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Carrot',         (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Broccoli',       (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Spinach',        (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'bag'),
    (NULL, 'en', 'Apple',          (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Banana',         (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'bunch'),
    (NULL, 'en', 'Orange',         (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Lemon',          (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Avocado',        (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Cucumber',       (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Bell Pepper',    (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Mushrooms',      (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'g'),
    (NULL, 'en', 'Sweet Potato',   (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'kg'),
    (NULL, 'en', 'Strawberry',     (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'box'),
    (NULL, 'en', 'Mango',          (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    (NULL, 'en', 'Watermelon',     (SELECT id FROM catalog.categories WHERE name = 'Fruits & Vegetables'      AND language = 'en'), 'unit'),
    -- Meat & Fish
    (NULL, 'en', 'Chicken Breast', (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Ground Beef',    (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Beef Steak',     (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Pork Ribs',      (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Salmon',         (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Shrimp',         (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Bacon',          (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'pack'),
    (NULL, 'en', 'Chicken Thigh',  (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'kg'),
    (NULL, 'en', 'Sausage',        (SELECT id FROM catalog.categories WHERE name = 'Meat & Fish'              AND language = 'en'), 'pack'),
    -- Bread & Bakery
    (NULL, 'en', 'White Bread',    (SELECT id FROM catalog.categories WHERE name = 'Bread & Bakery'           AND language = 'en'), 'unit'),
    (NULL, 'en', 'Whole Wheat Bread',(SELECT id FROM catalog.categories WHERE name = 'Bread & Bakery'         AND language = 'en'), 'unit'),
    (NULL, 'en', 'Hamburger Buns', (SELECT id FROM catalog.categories WHERE name = 'Bread & Bakery'           AND language = 'en'), 'pack'),
    (NULL, 'en', 'Croissant',      (SELECT id FROM catalog.categories WHERE name = 'Bread & Bakery'           AND language = 'en'), 'unit'),
    -- Dairy & Eggs
    (NULL, 'en', 'Milk',           (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'liter'),
    (NULL, 'en', 'Cheese',         (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'kg'),
    (NULL, 'en', 'Yogurt',         (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'unit'),
    (NULL, 'en', 'Butter',         (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'pack'),
    (NULL, 'en', 'Eggs',           (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'dozen'),
    (NULL, 'en', 'Cream Cheese',   (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'pack'),
    (NULL, 'en', 'Heavy Cream',    (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'liter'),
    (NULL, 'en', 'Mozzarella',     (SELECT id FROM catalog.categories WHERE name = 'Dairy & Eggs'             AND language = 'en'), 'pack'),
    -- Grains & Cereals
    (NULL, 'en', 'Rice',           (SELECT id FROM catalog.categories WHERE name = 'Grains & Cereals'         AND language = 'en'), 'kg'),
    (NULL, 'en', 'Pasta',          (SELECT id FROM catalog.categories WHERE name = 'Grains & Cereals'         AND language = 'en'), 'pack'),
    (NULL, 'en', 'Oats',           (SELECT id FROM catalog.categories WHERE name = 'Grains & Cereals'         AND language = 'en'), 'kg'),
    (NULL, 'en', 'Flour',          (SELECT id FROM catalog.categories WHERE name = 'Grains & Cereals'         AND language = 'en'), 'kg'),
    (NULL, 'en', 'Black Beans',    (SELECT id FROM catalog.categories WHERE name = 'Grains & Cereals'         AND language = 'en'), 'kg'),
    (NULL, 'en', 'Lentils',        (SELECT id FROM catalog.categories WHERE name = 'Grains & Cereals'         AND language = 'en'), 'kg'),
    -- Beverages
    (NULL, 'en', 'Water',          (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'liter'),
    (NULL, 'en', 'Orange Juice',   (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'liter'),
    (NULL, 'en', 'Coffee',         (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'pack'),
    (NULL, 'en', 'Tea',            (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'box'),
    (NULL, 'en', 'Soda',           (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'liter'),
    (NULL, 'en', 'Beer',           (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'unit'),
    (NULL, 'en', 'Wine',           (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Energy Drink',   (SELECT id FROM catalog.categories WHERE name = 'Beverages'                AND language = 'en'), 'unit'),
    -- Ingredients & Condiments
    (NULL, 'en', 'Salt',           (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'kg'),
    (NULL, 'en', 'Black Pepper',   (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'pack'),
    (NULL, 'en', 'Olive Oil',      (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'liter'),
    (NULL, 'en', 'Vegetable Oil',  (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'liter'),
    (NULL, 'en', 'Vinegar',        (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'liter'),
    (NULL, 'en', 'Soy Sauce',      (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Ketchup',        (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Mustard',        (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Mayonnaise',     (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'jar'),
    (NULL, 'en', 'Sugar',          (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'kg'),
    (NULL, 'en', 'Honey',          (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'jar'),
    (NULL, 'en', 'Tomato Paste',   (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'can'),
    (NULL, 'en', 'Tomato Sauce',   (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'jar'),
    (NULL, 'en', 'Cumin',          (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'pack'),
    (NULL, 'en', 'Oregano',        (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'pack'),
    (NULL, 'en', 'Paprika',        (SELECT id FROM catalog.categories WHERE name = 'Ingredients & Condiments' AND language = 'en'), 'pack'),
    -- Sweets & Candy
    (NULL, 'en', 'Chocolate',      (SELECT id FROM catalog.categories WHERE name = 'Sweets & Candy'           AND language = 'en'), 'unit'),
    (NULL, 'en', 'Cookies',        (SELECT id FROM catalog.categories WHERE name = 'Sweets & Candy'           AND language = 'en'), 'pack'),
    (NULL, 'en', 'Ice Cream',      (SELECT id FROM catalog.categories WHERE name = 'Sweets & Candy'           AND language = 'en'), 'unit'),
    (NULL, 'en', 'Granola Bar',    (SELECT id FROM catalog.categories WHERE name = 'Sweets & Candy'           AND language = 'en'), 'pack'),
    -- Cleaning & Hygiene
    (NULL, 'en', 'Dish Soap',      (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Laundry Detergent',(SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'     AND language = 'en'), 'pack'),
    (NULL, 'en', 'Fabric Softener',(SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Bleach',         (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Sponge',         (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'pack'),
    (NULL, 'en', 'Toilet Paper',   (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'pack'),
    (NULL, 'en', 'Paper Towels',   (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'roll'),
    (NULL, 'en', 'Shampoo',        (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Conditioner',    (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'bottle'),
    (NULL, 'en', 'Soap',           (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'unit'),
    (NULL, 'en', 'Toothpaste',     (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'tube'),
    (NULL, 'en', 'Deodorant',      (SELECT id FROM catalog.categories WHERE name = 'Cleaning & Hygiene'       AND language = 'en'), 'unit'),
    -- Chips & Snacks
    (NULL, 'en', 'Chips',          (SELECT id FROM catalog.categories WHERE name = 'Chips & Snacks'           AND language = 'en'), 'pack'),
    (NULL, 'en', 'Popcorn',        (SELECT id FROM catalog.categories WHERE name = 'Chips & Snacks'           AND language = 'en'), 'pack'),
    (NULL, 'en', 'Peanuts',        (SELECT id FROM catalog.categories WHERE name = 'Chips & Snacks'           AND language = 'en'), 'pack'),
    (NULL, 'en', 'Crackers',       (SELECT id FROM catalog.categories WHERE name = 'Chips & Snacks'           AND language = 'en'), 'pack'),
    -- Pet Supplies
    (NULL, 'en', 'Dog Food',       (SELECT id FROM catalog.categories WHERE name = 'Pet Supplies'             AND language = 'en'), 'kg'),
    (NULL, 'en', 'Cat Food',       (SELECT id FROM catalog.categories WHERE name = 'Pet Supplies'             AND language = 'en'), 'kg'),
    (NULL, 'en', 'Cat Litter',     (SELECT id FROM catalog.categories WHERE name = 'Pet Supplies'             AND language = 'en'), 'kg');

-- Seed: pt-BR global items
INSERT INTO catalog.items (household_id, language, name, category_id, default_unit) VALUES
    -- Frutas & Legumes
    (NULL, 'pt-BR', 'Alface',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Tomate',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Cebola',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Alho',             (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Batata',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Cenoura',          (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Brócolis',        (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Espinafre',        (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'maço'),
    (NULL, 'pt-BR', 'Maçã',            (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Banana',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'cacho'),
    (NULL, 'pt-BR', 'Laranja',          (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Limão',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Abacate',          (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Pepino',           (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Pimentão',        (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Cogumelo',         (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'g'),
    (NULL, 'pt-BR', 'Batata-Doce',      (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Morango',          (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'caixa'),
    (NULL, 'pt-BR', 'Manga',            (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Melancia',         (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Abacaxi',          (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Uva',              (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Abobrinha',        (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Beterraba',        (SELECT id FROM catalog.categories WHERE name = 'Frutas & Legumes'          AND language = 'pt-BR'), 'kg'),
    -- Carne & Peixe
    (NULL, 'pt-BR', 'Peito de Frango',  (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Carne Moída',      (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Filé de Boi',     (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Costela',          (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Salmão',          (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Tilápia',         (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Camarão',         (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Bacon',            (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Sobrecoxa',        (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Linguiça',        (SELECT id FROM catalog.categories WHERE name = 'Carne & Peixe'             AND language = 'pt-BR'), 'kg'),
    -- Pão & Confeitaria
    (NULL, 'pt-BR', 'Pão de Forma',     (SELECT id FROM catalog.categories WHERE name = 'Pão & Confeitaria'         AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Pão Integral',     (SELECT id FROM catalog.categories WHERE name = 'Pão & Confeitaria'         AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Pão Francês',     (SELECT id FROM catalog.categories WHERE name = 'Pão & Confeitaria'         AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Croissant',        (SELECT id FROM catalog.categories WHERE name = 'Pão & Confeitaria'         AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Pão de Hambúrguer',(SELECT id FROM catalog.categories WHERE name = 'Pão & Confeitaria'         AND language = 'pt-BR'), 'pacote'),
    -- Laticínios
    (NULL, 'pt-BR', 'Leite',            (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Queijo',           (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Iogurte',          (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Manteiga',         (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Ovos',             (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'dúzia'),
    (NULL, 'pt-BR', 'Cream Cheese',     (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Creme de Leite',   (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'caixa'),
    (NULL, 'pt-BR', 'Requeijão',       (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'pote'),
    (NULL, 'pt-BR', 'Muçarela',        (SELECT id FROM catalog.categories WHERE name = 'Laticínios'               AND language = 'pt-BR'), 'pacote'),
    -- Cereais & Grãos
    (NULL, 'pt-BR', 'Arroz',            (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Macarrão',        (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Aveia',            (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Farinha de Trigo', (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Feijão',           (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Fubá',            (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Lentilha',         (SELECT id FROM catalog.categories WHERE name = 'Cereais & Grãos'          AND language = 'pt-BR'), 'kg'),
    -- Bebidas
    (NULL, 'pt-BR', 'Água',            (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Suco de Laranja',  (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Café',            (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Chá',             (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'caixa'),
    (NULL, 'pt-BR', 'Refrigerante',     (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Cerveja',          (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Vinho',            (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'garrafa'),
    (NULL, 'pt-BR', 'Energético',      (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Coca-Cola',        (SELECT id FROM catalog.categories WHERE name = 'Bebidas'                   AND language = 'pt-BR'), 'litro'),
    -- Ingredientes & Condimentos
    (NULL, 'pt-BR', 'Sal',              (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Pimenta-do-Reino', (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Azeite',           (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Óleo',            (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Vinagre',          (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Molho Shoyu',      (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'garrafa'),
    (NULL, 'pt-BR', 'Ketchup',          (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'garrafa'),
    (NULL, 'pt-BR', 'Mostarda',         (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'garrafa'),
    (NULL, 'pt-BR', 'Maionese',         (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'pote'),
    (NULL, 'pt-BR', 'Açúcar',          (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Mel',              (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'pote'),
    (NULL, 'pt-BR', 'Extrato de Tomate',(SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'lata'),
    (NULL, 'pt-BR', 'Molho de Tomate',  (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'vidro'),
    (NULL, 'pt-BR', 'Cominho',          (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Orégano',         (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Páprica',         (SELECT id FROM catalog.categories WHERE name = 'Ingredientes & Condimentos' AND language = 'pt-BR'), 'pacote'),
    -- Lanches & Doces
    (NULL, 'pt-BR', 'Chocolate',        (SELECT id FROM catalog.categories WHERE name = 'Lanches & Doces'            AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Biscoito',         (SELECT id FROM catalog.categories WHERE name = 'Lanches & Doces'            AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Sorvete',          (SELECT id FROM catalog.categories WHERE name = 'Lanches & Doces'            AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Barra de Cereal',  (SELECT id FROM catalog.categories WHERE name = 'Lanches & Doces'            AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Bolo',             (SELECT id FROM catalog.categories WHERE name = 'Lanches & Doces'            AND language = 'pt-BR'), 'unidade'),
    -- Limpeza & Higiene
    (NULL, 'pt-BR', 'Detergente',       (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Sabão em Pó',      (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Amaciante',        (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Água Sanitária',   (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'litro'),
    (NULL, 'pt-BR', 'Esponja',          (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Papel Higiênico', (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Papel Toalha',     (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'rolo'),
    (NULL, 'pt-BR', 'Shampoo',          (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Condicionador',    (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Sabonete',         (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Pasta de Dente',   (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Desodorante',      (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    (NULL, 'pt-BR', 'Multiuso',         (SELECT id FROM catalog.categories WHERE name = 'Limpeza & Higiene'          AND language = 'pt-BR'), 'unidade'),
    -- Petiscos
    (NULL, 'pt-BR', 'Batata Frita',     (SELECT id FROM catalog.categories WHERE name = 'Petiscos'                   AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Pipoca',           (SELECT id FROM catalog.categories WHERE name = 'Petiscos'                   AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Amendoim',         (SELECT id FROM catalog.categories WHERE name = 'Petiscos'                   AND language = 'pt-BR'), 'pacote'),
    (NULL, 'pt-BR', 'Bolacha Salgada',  (SELECT id FROM catalog.categories WHERE name = 'Petiscos'                   AND language = 'pt-BR'), 'pacote'),
    -- Animais
    (NULL, 'pt-BR', 'Ração para Cachorro',(SELECT id FROM catalog.categories WHERE name = 'Animais'                  AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Ração para Gato',  (SELECT id FROM catalog.categories WHERE name = 'Animais'                   AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Areia para Gato',  (SELECT id FROM catalog.categories WHERE name = 'Animais'                   AND language = 'pt-BR'), 'kg'),
    (NULL, 'pt-BR', 'Petisco para Pet', (SELECT id FROM catalog.categories WHERE name = 'Animais'                   AND language = 'pt-BR'), 'pacote');

-- lists
CREATE SCHEMA IF NOT EXISTS lists;

CREATE TABLE lists.lists (
    id           UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    household_id UUID        NOT NULL,
    name         TEXT        NOT NULL,
    created_by   UUID        NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE lists.items (
    id              UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    list_id         UUID        NOT NULL REFERENCES lists.lists(id) ON DELETE CASCADE,
    name            TEXT        NOT NULL,
    catalog_item_id UUID        REFERENCES catalog.items(id) ON DELETE SET NULL,
    category_id     UUID        REFERENCES catalog.categories(id) ON DELETE SET NULL,
    added_by        UUID,
    sort_order      INTEGER     NOT NULL DEFAULT 1000,
    is_completed    BOOLEAN     NOT NULL DEFAULT false,
    completed_by    UUID,
    completed_at    TIMESTAMPTZ
);

CREATE INDEX ix_lists_items_list_id   ON lists.items (list_id);
CREATE INDEX ix_lists_lists_household ON lists.lists (household_id);

-- tasks
CREATE SCHEMA IF NOT EXISTS tasks;

CREATE TABLE tasks.recurring_tasks (
    id                  UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
    id                UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
    id           UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
    id         UUID    NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    recipe_id  UUID    NOT NULL REFERENCES recipes.recipes(id) ON DELETE CASCADE,
    name       TEXT    NOT NULL,
    quantity   TEXT,
    unit       TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE recipes.instructions (
    id         UUID    NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
    id                   UUID        NOT NULL DEFAULT uuidv7() PRIMARY KEY,
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
