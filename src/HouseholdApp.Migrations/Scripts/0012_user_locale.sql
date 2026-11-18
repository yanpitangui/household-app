-- The language each user reads the app in, so messages built outside their requests (push
-- notifications) use it too. Filled in by the web app on the user's next signed-in request.
ALTER TABLE identity.users
    ADD COLUMN locale TEXT NULL;
