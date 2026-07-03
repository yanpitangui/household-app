CREATE SCHEMA IF NOT EXISTS notifications;

CREATE TABLE notifications.push_subscriptions (
    endpoint   TEXT        NOT NULL PRIMARY KEY,
    user_id    UUID        NOT NULL,
    p256dh     TEXT        NOT NULL,
    auth       TEXT        NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_push_subscriptions_user_id ON notifications.push_subscriptions (user_id);
