-- f_unaccent originally had no fixed search_path, so it resolved unaccent()
-- via whatever search_path was active at call time. Autovacuum/auto-ANALYZE
-- background workers run with a minimal search_path (pg_catalog only), so
-- when they evaluate the catalog_items_name_trgm index expression, unaccent()
-- (installed in schema public) can't be found: "function unaccent(text) does
-- not exist". Pin the search_path so the function always resolves it.
CREATE OR REPLACE FUNCTION f_unaccent(text)
RETURNS text
LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE STRICT
SET search_path = public, pg_catalog
AS $$
BEGIN
    RETURN unaccent($1);
END;
$$;
