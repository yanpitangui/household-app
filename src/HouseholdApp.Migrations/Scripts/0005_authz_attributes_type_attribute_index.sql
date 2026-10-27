-- Mirrors Valtuutus 2.2.0 migration 20261024190000_lookup_indexes.sql.
-- Serves attribute-wide reads (every entity of a type with a given attribute),
-- which authz_idx_attributes can only bound by entity_type.
CREATE INDEX IF NOT EXISTS authz_idx_attributes_type_attribute
    ON authz.attributes ("entity_type", "attribute", "entity_id") INCLUDE ("value");
