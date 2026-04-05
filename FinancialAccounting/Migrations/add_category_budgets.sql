CREATE TABLE IF NOT EXISTS category_budgets (
    id           SERIAL PRIMARY KEY,
    category_id  INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    amount       NUMERIC(18, 2) NOT NULL CHECK (amount >= 0),
    created_at   TIMESTAMP DEFAULT NOW(),
    updated_at   TIMESTAMP DEFAULT NOW(),
    UNIQUE (category_id)
);
