-- Every household decides "today" in its own zone; existing households are in Brazil.
ALTER TABLE households.households
    ADD COLUMN time_zone TEXT NOT NULL DEFAULT 'America/Sao_Paulo';

-- Member rows used to be upserted only, so removed members stayed listed after Valtuutus dropped their access.
-- Valtuutus holds the live memberships; rows without a live tuple are removals that never reached this table.
DELETE FROM households.members m
WHERE NOT EXISTS (
    SELECT 1 FROM authz.relation_tuples t
    WHERE t.entity_type = 'household'
      AND t.entity_id = m.household_id::text
      AND t.subject_type = 'user'
      AND t.subject_id = m.user_id::text
      AND t.deleted_tx_id IS NULL);
