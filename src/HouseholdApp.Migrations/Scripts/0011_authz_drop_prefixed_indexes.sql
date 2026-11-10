-- Valtuutus owns the authz schema through its Weasel package, which runs at startup after DbUp
-- and creates these indexes under the names in Valtuutus's own scripts (idx_*, unique_attributes).
DROP INDEX IF EXISTS authz.authz_idx_created_at;
DROP INDEX IF EXISTS authz.authz_idx_attributes;
DROP INDEX IF EXISTS authz.authz_unique_attributes;
DROP INDEX IF EXISTS authz.authz_idx_attributes_type_attribute;
DROP INDEX IF EXISTS authz.authz_idx_tuples_entity_relation;
DROP INDEX IF EXISTS authz.authz_idx_tuples_subject_entities;
DROP INDEX IF EXISTS authz.authz_idx_tuples_user;
DROP INDEX IF EXISTS authz.authz_idx_tuples_userset;
DROP INDEX IF EXISTS authz.authz_idx_tuples_direct;
DROP INDEX IF EXISTS authz.authz_idx_tuples_indirect;
