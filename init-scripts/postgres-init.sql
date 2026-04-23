-- PostgreSQL Initialization Script
-- Comprehensive edge-case test data for migration testing
-- Covers: all mapped types (including PG-native: BOOLEAN, UUID, JSON/JSONB, BYTEA, INTERVAL, SERIAL),
--         numeric/string/datetime/binary edge cases, NULL handling,
--         reserved-word column names, composite PK, multiple UNIQUE constraints,
--         3-level FK chain, self-referencing FK, empty table, wide table, batch test (>1000 rows)
-- Idempotent: safe to re-run

-- ============================================================
-- DROP all tables in FK-dependency order (CASCADE handles FKs automatically)
-- ============================================================
DROP TABLE IF EXISTS migration_test.fk_child            CASCADE;
DROP TABLE IF EXISTS migration_test.fk_parent           CASCADE;
DROP TABLE IF EXISTS migration_test.fk_grandparent      CASCADE;
DROP TABLE IF EXISTS migration_test.orders              CASCADE;
DROP TABLE IF EXISTS migration_test.audit_log           CASCADE;
DROP TABLE IF EXISTS migration_test.products            CASCADE;
DROP TABLE IF EXISTS migration_test.users               CASCADE;
DROP TABLE IF EXISTS migration_test.self_ref            CASCADE;
DROP TABLE IF EXISTS migration_test.type_coverage       CASCADE;
DROP TABLE IF EXISTS migration_test.numeric_edge_cases  CASCADE;
DROP TABLE IF EXISTS migration_test.string_edge_cases   CASCADE;
DROP TABLE IF EXISTS migration_test.datetime_edge_cases CASCADE;
DROP TABLE IF EXISTS migration_test.binary_edge_cases   CASCADE;
DROP TABLE IF EXISTS migration_test.null_edge_cases     CASCADE;
DROP TABLE IF EXISTS migration_test.reserved_words_cols CASCADE;
DROP TABLE IF EXISTS migration_test.composite_pk        CASCADE;
DROP TABLE IF EXISTS migration_test.multi_unique        CASCADE;
DROP TABLE IF EXISTS migration_test.empty_table         CASCADE;
DROP TABLE IF EXISTS migration_test.wide_table          CASCADE;
DROP TABLE IF EXISTS migration_test.batch_test          CASCADE;

CREATE SCHEMA IF NOT EXISTS migration_test;

-- ============================================================
-- SECTION 1: Original tables (users, products, orders, audit_log)
-- ============================================================
CREATE TABLE migration_test.users (
    id            SERIAL PRIMARY KEY,
    username      VARCHAR(50)  NOT NULL UNIQUE,
    email         VARCHAR(100) NOT NULL,
    password_hash BYTEA,
    created_at    TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
    updated_at    TIMESTAMP    DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE migration_test.products (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(255) NOT NULL,
    description TEXT,
    price       DECIMAL(10,2),
    image       BYTEA,
    thumbnail   BYTEA,
    created_at  TIMESTAMP    DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE migration_test.orders (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER NOT NULL REFERENCES migration_test.users(id),
    product_id  INTEGER NOT NULL REFERENCES migration_test.products(id),
    quantity    INTEGER NOT NULL,
    total_price DECIMAL(10,2),
    order_date  TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
    status      VARCHAR(50)  DEFAULT 'pending'
);

CREATE TABLE migration_test.audit_log (
    id               SERIAL PRIMARY KEY,
    table_name       VARCHAR(100),
    operation        VARCHAR(10),
    record_id        INTEGER,
    changed_by       VARCHAR(100),
    change_data      BYTEA,
    change_timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO migration_test.users (username, email, password_hash) VALUES
    ('alice',   'alice@example.com',   '\x6861736865645f70617373776f72645f31'::bytea),
    ('bob',     'bob@example.com',     '\x6861736865645f70617373776f72645f32'::bytea),
    ('charlie', 'charlie@example.com', '\x6861736865645f70617373776f72645f33'::bytea),
    ('diana',   'diana@example.com',   '\x6861736865645f70617373776f72645f34'::bytea);

INSERT INTO migration_test.products (name, description, price, image, thumbnail) VALUES
    ('Laptop Pro',          'High-performance laptop',  1299.99, '\x89504e470d0a1a0a'::bytea, '\x89504e470d0a1a0a'::bytea),
    ('Wireless Mouse',      'Ergonomic wireless mouse',   29.99, '\x89504e470d0a1a0a'::bytea, '\x89504e470d0a1a0a'::bytea),
    ('USB-C Hub',           'Multi-port USB-C hub',       49.99, '\x89504e470d0a1a0a'::bytea, '\x89504e470d0a1a0a'::bytea),
    ('Mechanical Keyboard', 'RGB mechanical keyboard',   149.99, '\x89504e470d0a1a0a'::bytea, '\x89504e470d0a1a0a'::bytea),
    ('Monitor 4K',          '27-inch 4K monitor',        399.99, '\x89504e470d0a1a0a'::bytea, '\x89504e470d0a1a0a'::bytea);

INSERT INTO migration_test.orders (user_id, product_id, quantity, total_price, status) VALUES
    (1, 1, 1, 1299.99, 'completed'), (1, 2, 2,   59.98, 'completed'),
    (2, 3, 1,   49.99, 'pending'),   (2, 4, 1,  149.99, 'shipped'),
    (3, 1, 1, 1299.99, 'processing'),(3, 5, 2,  799.98, 'pending'),
    (4, 2, 3,   89.97, 'completed'), (4, 4, 1,  149.99, 'completed');

INSERT INTO migration_test.audit_log (table_name, operation, record_id, changed_by, change_data) VALUES
    ('users',    'INSERT', 1, 'system', decode('7b226e616d65223a22416c696365227d',       'hex')),
    ('products', 'INSERT', 1, 'system', decode('7b226e616d65223a224c6170746f7020506f72227d', 'hex')),
    ('orders',   'INSERT', 1, 'system', decode('7b22737461747573223a226f70656e227d',     'hex')),
    ('orders',   'UPDATE', 1, 'system', decode('7b22737461747573223a226f72646572656422', 'hex'));

-- ============================================================
-- SECTION 2: Type Coverage — one column per PostgreSQL type handled by MapDataType
-- ============================================================
CREATE TABLE migration_test.type_coverage (
    id                     SERIAL PRIMARY KEY,
    -- Integer types (including aliases)
    col_integer            INTEGER          NULL,   -- int / int4
    col_bigint             BIGINT           NULL,   -- int8
    col_smallint           SMALLINT         NULL,   -- int2
    col_smallserial_like   SMALLINT         NULL,   -- smallserial equivalent (no auto-increment here)
    col_serial_like        INTEGER          NULL,   -- serial equivalent
    col_bigserial_like     BIGINT           NULL,   -- bigserial equivalent
    -- Numeric types
    col_numeric_10_2       NUMERIC(10,2)    NULL,
    col_numeric_no_prec    NUMERIC          NULL,   -- arbitrary precision
    col_decimal_15_5       DECIMAL(15,5)    NULL,
    col_double_precision   DOUBLE PRECISION NULL,   -- float8
    col_real               REAL             NULL,   -- float4
    col_money              MONEY            NULL,
    -- Character types
    col_varchar_100        VARCHAR(100)     NULL,
    col_char_varying_100   CHARACTER VARYING(100) NULL,  -- same as varchar
    col_text               TEXT             NULL,
    col_char_10            CHAR(10)         NULL,
    col_character_10       CHARACTER(10)    NULL,   -- same as char
    -- Boolean
    col_boolean            BOOLEAN          NULL,
    -- Binary
    col_bytea              BYTEA            NULL,
    -- UUID
    col_uuid               UUID             NULL,
    -- Date/time types
    col_date               DATE             NULL,
    col_time_0             TIME(0)          NULL,
    col_time_6             TIME(6)          NULL,
    col_timetz             TIMETZ           NULL,   -- time with time zone
    col_timestamp_0        TIMESTAMP(0)     NULL,
    col_timestamp_6        TIMESTAMP(6)     NULL,
    col_timestamptz_0      TIMESTAMPTZ(0)   NULL,
    col_timestamptz_6      TIMESTAMPTZ(6)   NULL,
    col_interval           INTERVAL         NULL,
    -- JSON types
    col_json               JSON             NULL,
    col_jsonb              JSONB            NULL,
    -- XML
    col_xml                XML              NULL
);

-- Row 1: representative values for every type
INSERT INTO migration_test.type_coverage (
    col_integer, col_bigint, col_smallint, col_smallserial_like, col_serial_like, col_bigserial_like,
    col_numeric_10_2, col_numeric_no_prec, col_decimal_15_5,
    col_double_precision, col_real, col_money,
    col_varchar_100, col_char_varying_100, col_text, col_char_10, col_character_10,
    col_boolean, col_bytea, col_uuid,
    col_date, col_time_0, col_time_6, col_timetz,
    col_timestamp_0, col_timestamp_6, col_timestamptz_0, col_timestamptz_6,
    col_interval, col_json, col_jsonb, col_xml
) VALUES (
    42, 9876543210, 1000, 100, 200, 9876543210,
    12345.67, 123456789012345678901234567890.123456789, 12345.12345,
    3.14159265358979, 2.71828, 1234.56::money,
    'varchar value', 'char varying value', 'long text value — can be unlimited length',
    'CHAR      ', 'CHARACTER ',
    TRUE, '\x48656c6c6f576f726c64'::bytea, 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'::uuid,
    '2024-06-15', '14:30:00', '14:30:00.123456', '14:30:00.123456+05:30',
    '2024-06-15 14:30:00', '2024-06-15 14:30:00.123456',
    '2024-06-15 14:30:00+02:00', '2024-06-15 14:30:00.123456+02:00',
    INTERVAL '1 year 2 months 3 days 4 hours 5 minutes 6.789 seconds',
    '{"key": "value", "num": 42, "arr": [1,2,3]}'::json,
    '{"key": "value", "nested": {"a": 1}}'::jsonb,
    '<root><item id="1">test &amp; value</item></root>'::xml
);

-- Row 2: all NULLs
INSERT INTO migration_test.type_coverage (col_integer) VALUES (NULL);

-- ============================================================
-- SECTION 3: Numeric Edge Cases
-- ============================================================
CREATE TABLE migration_test.numeric_edge_cases (
    id           SERIAL PRIMARY KEY,
    label        VARCHAR(60)      NOT NULL,
    col_integer  INTEGER          NULL,
    col_bigint   BIGINT           NULL,
    col_smallint SMALLINT         NULL,
    col_numeric  NUMERIC(38,10)   NULL,
    col_double   DOUBLE PRECISION NULL,
    col_real     REAL             NULL,
    col_money    MONEY            NULL
);

INSERT INTO migration_test.numeric_edge_cases
    (label, col_integer, col_bigint, col_smallint, col_numeric, col_double, col_real, col_money)
VALUES
    ('integer_max',         2147483647,  NULL,  NULL, NULL, NULL, NULL, NULL),
    ('integer_min',        -2147483648,  NULL,  NULL, NULL, NULL, NULL, NULL),
    ('integer_zero',                 0,  NULL,  NULL, NULL, NULL, NULL, NULL),
    ('bigint_max',               NULL,  9223372036854775807,  NULL, NULL, NULL, NULL, NULL),
    ('bigint_min',               NULL, -9223372036854775808,  NULL, NULL, NULL, NULL, NULL),
    ('smallint_max',             NULL,  NULL,  32767, NULL, NULL, NULL, NULL),
    ('smallint_min',             NULL,  NULL, -32768, NULL, NULL, NULL, NULL),
    ('numeric_high_prec',        NULL,  NULL,   NULL, 9999999999999999999999999999.9999999999, NULL, NULL, NULL),
    ('numeric_high_neg',         NULL,  NULL,   NULL, -9999999999999999999999999999.9999999999, NULL, NULL, NULL),
    ('numeric_zero',             NULL,  NULL,   NULL, 0.0000000000, NULL, NULL, NULL),
    ('numeric_tiny_frac',        NULL,  NULL,   NULL, 0.0000000001, NULL, NULL, NULL),
    ('double_max',               NULL,  NULL,   NULL, NULL, 1.7976931348623157E+308, NULL, NULL),
    ('double_min_positive',      NULL,  NULL,   NULL, NULL, 2.2250738585072014E-308, NULL, NULL),
    ('double_neg_max',           NULL,  NULL,   NULL, NULL, -1.7976931348623157E+308, NULL, NULL),
    ('double_zero',              NULL,  NULL,   NULL, NULL, 0.0, NULL, NULL),
    -- PostgreSQL-specific: NaN and Infinity in NUMERIC/FLOAT (cannot migrate to MSSQL/Oracle)
    ('double_infinity_pos',      NULL,  NULL,   NULL, NULL, 'Infinity'::double precision, NULL, NULL),
    ('double_infinity_neg',      NULL,  NULL,   NULL, NULL, '-Infinity'::double precision, NULL, NULL),
    ('double_nan',               NULL,  NULL,   NULL, NULL, 'NaN'::double precision, NULL, NULL),
    ('real_max',                 NULL,  NULL,   NULL, NULL, NULL, 3.40282347E+38, NULL),
    ('real_min_positive',        NULL,  NULL,   NULL, NULL, NULL, 1.17549435E-38, NULL),
    ('money_max',                NULL,  NULL,   NULL, NULL, NULL, NULL, '92233720368547758.07'::money),
    ('money_min',                NULL,  NULL,   NULL, NULL, NULL, NULL, '-92233720368547758.08'::money),
    ('all_null',                 NULL,  NULL,   NULL, NULL, NULL, NULL, NULL);

-- ============================================================
-- SECTION 4: String Edge Cases
-- ============================================================
CREATE TABLE migration_test.string_edge_cases (
    id              SERIAL PRIMARY KEY,
    label           VARCHAR(80)  NOT NULL,
    col_varchar     VARCHAR(8000) NULL,
    col_text        TEXT         NULL,
    col_char_10     CHAR(10)     NULL
);

INSERT INTO migration_test.string_edge_cases (label, col_varchar, col_text, col_char_10) VALUES
    -- NOTE: In PostgreSQL, empty string '' IS NOT NULL (unlike Oracle!)
    ('empty_string',        '',                         '',                         '          '),
    ('single_space',        ' ',                        ' ',                        ' '),
    ('single_quote',        'O''Brien''s',              'apostrophe''s in text',    'quote''    '),
    ('double_quote',        'say "hello"',              'double "quotes" in text',  '"quote"   '),
    ('backslash',           'C:\path\to\file',          'back\slash\path',          'back\     '),
    ('newline',             'line1' || chr(10) || 'line2',
                            'CR' || chr(13) || 'LF' || chr(10) || 'end',
                            NULL),
    ('tab_char',            'col1' || chr(9) || 'col2', 'a' || chr(9) || 'b',      NULL),
    -- NOTE: null byte (\x00) is NOT valid in PostgreSQL TEXT/VARCHAR (UTF-8) — covered only in binary_edge_cases (BYTEA)
    ('unicode_cjk',         '你好世界',                 '中文: 你好世界 — 日本語: こんにちは', NULL),
    ('unicode_arabic',      'مرحبا بالعالم',            'العربية: مرحبا',           NULL),
    ('unicode_emoji',       '😀🎉🚀💡🔥',              'Hello 😀 World 🌍',         NULL),
    ('unicode_diacritics',  'Ñoño Ünïcödë',            'Ñoño café naïve résumé',  NULL),
    ('unicode_rtl',         'LTR' || E'\u200F' || 'mixed', 'zero-width' || E'\u200B' || 'space', NULL),
    ('sql_injection',       '; DROP TABLE users; --',   'UNION SELECT * FROM pg_tables --', NULL),
    ('xml_html_chars',      '<root>&amp;</root>',       '<a href="x">link &amp; more</a>', NULL),
    ('dollar_sign',         'price: $100.00',           'total: $999.99',           NULL),
    ('percent_underscore',  '100% complete',            'LIKE _pattern% test',      NULL),   -- SQL LIKE metacharacters
    ('long_string_4001',    repeat('A', 4001),          repeat('B', 8001),          NULL),
    ('all_null',            NULL,                       NULL,                       NULL);

-- Very long text (>5000 chars) to test TEXT handling
INSERT INTO migration_test.string_edge_cases (label, col_varchar, col_text)
VALUES ('long_string_5600',
    NULL,
    repeat('Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor. ', 70));

-- ============================================================
-- SECTION 5: Datetime Edge Cases
-- ============================================================
CREATE TABLE migration_test.datetime_edge_cases (
    id               SERIAL PRIMARY KEY,
    label            VARCHAR(60)  NOT NULL,
    col_date         DATE         NULL,
    col_time_0       TIME(0)      NULL,
    col_time_6       TIME(6)      NULL,
    col_timetz       TIMETZ       NULL,  -- time with time zone
    col_timestamp_0  TIMESTAMP(0) NULL,
    col_timestamp_3  TIMESTAMP(3) NULL,
    col_timestamp_6  TIMESTAMP(6) NULL,
    col_timestamptz  TIMESTAMPTZ  NULL,
    col_interval     INTERVAL     NULL
);

INSERT INTO migration_test.datetime_edge_cases
    (label, col_date, col_time_0, col_time_6, col_timetz,
     col_timestamp_0, col_timestamp_3, col_timestamp_6, col_timestamptz, col_interval)
VALUES
    ('typical',
     '2024-06-15', '14:30:00', '14:30:00.123456', '14:30:00.123456+05:30',
     '2024-06-15 14:30:00', '2024-06-15 14:30:00.123', '2024-06-15 14:30:00.123456',
     '2024-06-15 14:30:00.123456+02:00',
     INTERVAL '1 year 2 months 3 days 4 hours 5 minutes 6.789 seconds'),
    ('midnight',
     '2024-01-01', '00:00:00', '00:00:00.000000', '00:00:00+00:00',
     '2024-01-01 00:00:00', '2024-01-01 00:00:00.000', '2024-01-01 00:00:00.000000',
     '2024-01-01 00:00:00+00:00', INTERVAL '0'),
    ('end_of_day',
     '2024-12-31', '23:59:59', '23:59:59.999999', '23:59:59.999999+14:00',
     '2024-12-31 23:59:59', '2024-12-31 23:59:59.999', '2024-12-31 23:59:59.999999',
     '2024-12-31 23:59:59.999999-12:00', INTERVAL '-1 second'),
    ('timestamp_min',
     '0001-01-01', '00:00:00', '00:00:00.000001', NULL,
     '0001-01-01 00:00:00', '0001-01-01 00:00:00.000', '0001-01-01 00:00:00.000001',
     '0001-01-01 00:00:00+00:00', INTERVAL '-178956970 years'),
    ('timestamp_max',
     '9999-12-31', '23:59:59', '23:59:59.999999', NULL,
     '9999-12-31 23:59:59', '9999-12-31 23:59:59.999', '9999-12-31 23:59:59.999999',
     '9999-12-31 23:59:59.999999+00:00', INTERVAL '178956970 years'),
    ('unix_epoch',
     '1970-01-01', '00:00:00', '00:00:00.000000', '00:00:00+00:00',
     '1970-01-01 00:00:00', '1970-01-01 00:00:00.000', '1970-01-01 00:00:00.000000',
     '1970-01-01 00:00:00+00:00', INTERVAL '0 seconds'),
    ('tz_positive_max',  NULL, NULL, NULL, '12:00:00+14:00',
     NULL, NULL, NULL, '2024-06-15 12:00:00+14:00', NULL),
    ('tz_negative_max',  NULL, NULL, NULL, '12:00:00-12:00',
     NULL, NULL, NULL, '2024-06-15 12:00:00-12:00', NULL),
    ('interval_years',   NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     INTERVAL '10 years 6 months'),
    ('interval_days',    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     INTERVAL '365 days 12 hours 30 minutes 45.123 seconds'),
    ('interval_negative',NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     INTERVAL '-1 year -6 months'),
    ('all_null',         NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);

-- ============================================================
-- SECTION 6: Binary Edge Cases
-- ============================================================
CREATE TABLE migration_test.binary_edge_cases (
    id            SERIAL PRIMARY KEY,
    label         VARCHAR(60) NOT NULL,
    col_bytea     BYTEA       NULL
);

INSERT INTO migration_test.binary_edge_cases (label, col_bytea) VALUES
    ('all_zeros',        '\x0000000000000000'::bytea),
    ('all_ff',           '\xffffffffffffffff'::bytea),
    ('sequential',       '\x000102030405060708090a0b0c0d0e0f'::bytea),
    ('text_as_bytes',    convert_to('Hello, World!', 'UTF8')),
    ('png_magic',        '\x89504e470d0a1a0a0000000d49484452'::bytea),
    ('pdf_magic',        '\x255044462d312e34'::bytea),
    ('guid_bytes',       '\x6ba7b8109dad11d180b400c04fd430c8'::bytea),
    ('single_null_byte', '\x00'::bytea),
    ('empty_bytes',      '\x'::bytea),   -- zero-length bytea (NOT NULL, just empty)
    ('null_value',       NULL);

-- ============================================================
-- SECTION 7: NULL Edge Cases
-- ============================================================
CREATE TABLE migration_test.null_edge_cases (
    id           SERIAL PRIMARY KEY,
    col_integer  INTEGER          NULL,
    col_bigint   BIGINT           NULL,
    col_numeric  NUMERIC(10,2)    NULL,
    col_double   DOUBLE PRECISION NULL,
    col_varchar  VARCHAR(100)     NULL,
    col_text     TEXT             NULL,
    col_boolean  BOOLEAN          NULL,
    col_date     DATE             NULL,
    col_timestamp TIMESTAMP       NULL,
    col_bytea    BYTEA            NULL,
    col_uuid     UUID             NULL,
    col_json     JSONB            NULL
);

INSERT INTO migration_test.null_edge_cases
    (col_integer, col_bigint, col_numeric, col_double, col_varchar, col_text, col_boolean, col_date, col_timestamp, col_bytea, col_uuid, col_json)
VALUES
    -- All non-null
    (1, 100, 9.99, 3.14, 'hello', 'text value', TRUE, '2024-01-01', '2024-01-01 12:00:00', '\x01020304'::bytea, 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'::uuid, '{"x":1}'::jsonb),
    -- All null
    (NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
    -- Mixed A
    (2, -1, 0.00, NULL, '', NULL, FALSE, '2000-02-29', NULL, NULL, NULL, NULL),
    -- Mixed B
    (NULL, NULL, NULL, -0.0::double precision, NULL, 'unicode 日本語', NULL, NULL, '1970-01-01 00:00:00', '\xff'::bytea, NULL, '{"arr":[1,2,3]}'::jsonb),
    -- Zero/false values
    (0, 0, 0.00, 0.0, '', '', FALSE, '1970-01-01', '1970-01-01 00:00:00', '\x00'::bytea, '00000000-0000-0000-0000-000000000000'::uuid, 'null'::jsonb),
    -- Extreme values
    (-2147483648, -9223372036854775808, -99999999.99, -1.0E+308, 'negative', 'min values', TRUE, '0001-01-01', '0001-01-01 00:00:00', '\xdeadbeef'::bytea, 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid, '{"min":true}'::jsonb);

-- ============================================================
-- SECTION 8: Reserved Word Column Names (quoted identifiers)
-- ============================================================
CREATE TABLE migration_test.reserved_words_cols (
    id       SERIAL PRIMARY KEY,
    "order"  INTEGER      NULL,
    "group"  VARCHAR(50)  NULL,
    "select" VARCHAR(50)  NULL,
    "from"   VARCHAR(100) NULL,
    "where"  VARCHAR(100) NULL,
    "key"    VARCHAR(50)  NULL,
    "value"  TEXT         NULL,
    "index"  INTEGER      NULL,
    "date"   DATE         NULL,
    "type"   VARCHAR(50)  NULL,
    "status" VARCHAR(50)  NULL,
    "level"  INTEGER      NULL,
    "name"   VARCHAR(100) NULL,
    "user"   VARCHAR(50)  NULL,
    "table"  VARCHAR(50)  NULL
);

INSERT INTO migration_test.reserved_words_cols
    ("order","group","select","from","where","key","value","index","date","type","status","level","name","user","table")
VALUES
    (1, 'admin',   'SELECT 1', 'table_a', 'id = 1', 'key001', '{"x":1}', 0, '2024-01-01', 'typeA', 'active',   1, 'Alice', 'postgres', 'users'),
    (2, 'editors', 'SELECT *', 'table_b', 'id > 0', 'key002', '[1,2,3]', 1, '2024-06-15', 'typeB', 'inactive', 5, 'Bob',   'app',      'products'),
    (NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);

-- ============================================================
-- SECTION 9: Composite Primary Key (3-column PK)
-- ============================================================
CREATE TABLE migration_test.composite_pk (
    tenant_id   INTEGER      NOT NULL,
    entity_type VARCHAR(50)  NOT NULL,
    entity_id   BIGINT       NOT NULL,
    label       VARCHAR(100) NULL,
    payload     JSONB        NULL,
    created_at  TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_composite_pk PRIMARY KEY (tenant_id, entity_type, entity_id)
);

INSERT INTO migration_test.composite_pk (tenant_id, entity_type, entity_id, label, payload) VALUES
    (1, 'user',    100, 'Tenant1 User 100',    '{"role":"admin"}'::jsonb),
    (1, 'user',    101, 'Tenant1 User 101',    '{"role":"viewer"}'::jsonb),
    (1, 'product', 200, 'Tenant1 Product 200', '{"sku":"SKU-200"}'::jsonb),
    (2, 'user',    100, 'Tenant2 User 100',    '{"role":"editor"}'::jsonb),
    (2, 'order',   300, 'Tenant2 Order 300',   NULL),
    (3, 'product', 200, 'Tenant3 Product 200', '{"sku":"SKU-200"}'::jsonb);

-- ============================================================
-- SECTION 10: Multiple UNIQUE Constraints on different columns
-- ============================================================
CREATE TABLE migration_test.multi_unique (
    id        SERIAL PRIMARY KEY,
    sku       VARCHAR(50)  NOT NULL,
    barcode   VARCHAR(50)  NOT NULL,
    serial_no VARCHAR(100) NULL,
    email     VARCHAR(100) NULL,
    label     VARCHAR(200) NULL,
    CONSTRAINT uq_multi_unique_sku     UNIQUE (sku),
    CONSTRAINT uq_multi_unique_barcode UNIQUE (barcode),
    CONSTRAINT uq_multi_unique_serial  UNIQUE (serial_no),
    CONSTRAINT uq_multi_unique_email   UNIQUE (email)
);

INSERT INTO migration_test.multi_unique (sku, barcode, serial_no, email, label) VALUES
    ('SKU-001', 'BAR-001', 'SN-001', 'prod1@example.com', 'Product 1'),
    ('SKU-002', 'BAR-002', 'SN-002', 'prod2@example.com', 'Product 2'),
    ('SKU-003', 'BAR-003', NULL,     NULL,                'Product 3 – no serial/email'),
    ('SKU-004', 'BAR-004', 'SN-004', 'prod4@example.com', 'Product 4');

-- ============================================================
-- SECTION 11: 3-Level FK Chain (grandparent → parent → child)
-- ============================================================
CREATE TABLE migration_test.fk_grandparent (
    id   SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    code VARCHAR(20)  NOT NULL UNIQUE
);

CREATE TABLE migration_test.fk_parent (
    id             SERIAL PRIMARY KEY,
    grandparent_id INTEGER NOT NULL REFERENCES migration_test.fk_grandparent(id),
    name           VARCHAR(100) NOT NULL,
    description    VARCHAR(500) NULL
);

CREATE TABLE migration_test.fk_child (
    id         SERIAL PRIMARY KEY,
    parent_id  INTEGER NOT NULL REFERENCES migration_test.fk_parent(id),
    name       VARCHAR(100)  NOT NULL,
    value      DECIMAL(10,2) NULL,
    created_at TIMESTAMP     DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO migration_test.fk_grandparent (name, code) VALUES
    ('Region A', 'REG-A'), ('Region B', 'REG-B'), ('Region C', 'REG-C');

INSERT INTO migration_test.fk_parent (grandparent_id, name, description) VALUES
    (1, 'City A1', 'City in Region A'), (1, 'City A2', 'Another city in Region A'),
    (2, 'City B1', 'City in Region B'), (3, 'City C1', 'City in Region C');

INSERT INTO migration_test.fk_child (parent_id, name, value) VALUES
    (1, 'District A1-1', 100.00), (1, 'District A1-2', 200.00),
    (2, 'District A2-1', 150.00), (3, 'District B1-1', 300.00),
    (4, 'District C1-1', 250.00);

-- ============================================================
-- SECTION 12: Self-Referencing FK (category tree)
-- ============================================================
CREATE TABLE migration_test.self_ref (
    id        SERIAL PRIMARY KEY,
    parent_id INTEGER      NULL REFERENCES migration_test.self_ref(id),
    name      VARCHAR(100) NOT NULL,
    depth     INTEGER      NOT NULL DEFAULT 0,
    path      VARCHAR(500) NULL
);

INSERT INTO migration_test.self_ref (parent_id, name, depth, path) VALUES
    (NULL, 'Electronics', 0, '/Electronics'),
    (NULL, 'Clothing',    0, '/Clothing'),
    (NULL, 'Books',       0, '/Books');

INSERT INTO migration_test.self_ref (parent_id, name, depth, path) VALUES
    (1, 'Computers', 1, '/Electronics/Computers'),
    (1, 'Phones',    1, '/Electronics/Phones'),
    (1, 'Audio',     1, '/Electronics/Audio'),
    (2, 'Men',       1, '/Clothing/Men'),
    (2, 'Women',     1, '/Clothing/Women');

INSERT INTO migration_test.self_ref (parent_id, name, depth, path) VALUES
    (4, 'Laptops',     2, '/Electronics/Computers/Laptops'),
    (4, 'Desktops',    2, '/Electronics/Computers/Desktops'),
    (5, 'Smartphones', 2, '/Electronics/Phones/Smartphones'),
    (7, 'Shirts',      2, '/Clothing/Men/Shirts'),
    (8, 'Dresses',     2, '/Clothing/Women/Dresses');

-- ============================================================
-- SECTION 13: Empty Table (Schema-Only migration test)
-- ============================================================
CREATE TABLE migration_test.empty_table (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(100)  NOT NULL,
    description TEXT          NULL,
    value       NUMERIC(18,6) NULL,
    active      BOOLEAN       NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMP     DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_empty_table_name UNIQUE (name)
);
-- Intentionally no rows — used for Schema Only migration tests

-- ============================================================
-- SECTION 14: Wide Table (30 columns)
-- ============================================================
CREATE TABLE migration_test.wide_table (
    id      SERIAL PRIMARY KEY,
    col_01  VARCHAR(50)      NULL, col_02  VARCHAR(50)      NULL,
    col_03  INTEGER          NULL, col_04  INTEGER          NULL,
    col_05  NUMERIC(10,2)    NULL, col_06  NUMERIC(10,2)    NULL,
    col_07  TIMESTAMP        NULL, col_08  TIMESTAMP        NULL,
    col_09  BOOLEAN          NULL, col_10  BOOLEAN          NULL,
    col_11  TEXT             NULL, col_12  TEXT             NULL,
    col_13  BIGINT           NULL, col_14  BIGINT           NULL,
    col_15  DOUBLE PRECISION NULL, col_16  DOUBLE PRECISION NULL,
    col_17  DATE             NULL, col_18  DATE             NULL,
    col_19  TIME(3)          NULL, col_20  TIME(3)          NULL,
    col_21  BYTEA            NULL, col_22  UUID             NULL,
    col_23  TEXT             NULL, col_24  SMALLINT         NULL,
    col_25  SMALLINT         NULL, col_26  CHAR(5)          NULL,
    col_27  NUMERIC(19,4)    NULL, col_28  REAL             NULL,
    col_29  VARCHAR(100)     NULL, col_30  XML              NULL
);

INSERT INTO migration_test.wide_table
    (col_01,col_02,col_03,col_04,col_05,col_06,col_07,col_08,col_09,col_10,
     col_11,col_12,col_13,col_14,col_15,col_16,col_17,col_18,col_19,col_20,
     col_21,col_22,col_23,col_24,col_25,col_26,col_27,col_28,col_29,col_30)
VALUES
    ('alpha','beta',1,2,1.11,2.22,'2024-01-01','2024-06-15',TRUE,FALSE,
     'unicode 日本語','emoji 😀',9999999,-9999999,1.1,2.2,'2024-01-01','2024-06-15','10:30:00.123','23:59:59.999',
     '\xdeadbeef'::bytea,'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'::uuid,repeat('X',1000),32000,200,'ABCDE',9999.9999,3.14::real,
     'wide_test','<r><v>1</v></r>'::xml),
    (NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
     NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
     NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

-- ============================================================
-- SECTION 15: Batch Test (1200 rows — verifies 1000-row batch processing)
-- ============================================================
CREATE TABLE migration_test.batch_test (
    id         SERIAL PRIMARY KEY,
    seq_num    INTEGER        NOT NULL,
    label      VARCHAR(50)    NOT NULL,
    value_int  INTEGER        NULL,
    value_dec  NUMERIC(12,4)  NULL,
    value_str  VARCHAR(100)   NULL,
    created_at TIMESTAMP      DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO migration_test.batch_test (seq_num, label, value_int, value_dec, value_str)
SELECT
    n,
    'Row_' || lpad(n::text, 4, '0'),
    n % 1000,
    n * 3.14159,
    CASE n % 5
        WHEN 0 THEN 'fizzbuzz' WHEN 1 THEN 'one'
        WHEN 2 THEN 'two'      WHEN 3 THEN 'three'
        ELSE 'four'
    END
FROM generate_series(1, 1200) AS n;
