-- Access tokens let other apps act for one member in one household. Only a SHA-256 hash of each token is kept.
CREATE SCHEMA IF NOT EXISTS integrations;

CREATE TABLE integrations.access_tokens (
    id               UUID        NOT NULL PRIMARY KEY,
    user_id          UUID        NOT NULL,
    household_id     UUID        NOT NULL,
    name             TEXT        NOT NULL,
    token_hash       BYTEA       NOT NULL,
    token_hint       TEXT        NOT NULL,
    scopes           TEXT[]      NOT NULL,
    default_list_id  UUID        NULL,
    default_group_id UUID        NULL,
    created_at       TIMESTAMPTZ NOT NULL,
    expires_at       TIMESTAMPTZ NOT NULL,
    last_used_at     TIMESTAMPTZ NULL,
    revoked_at       TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX ux_access_tokens_token_hash ON integrations.access_tokens (token_hash);
CREATE INDEX ix_access_tokens_user_household ON integrations.access_tokens (user_id, household_id);
