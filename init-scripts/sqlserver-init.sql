-- SQL Server Database Initialization Script
-- Comprehensive edge-case test data for migration testing
-- Covers: all mapped types, numeric/string/datetime/binary edge cases,
--         NULL handling, reserved-word column names, composite PK,
--         multiple UNIQUE constraints, 3-level FK chain, self-referencing FK,
--         empty table (schema-only), wide table, batch test (>1000 rows)
-- Idempotent: safe to re-run

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'TestDB')
BEGIN
    CREATE DATABASE TestDB;
END
GO

USE TestDB;
GO

IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'migration_test')
BEGIN
    EXEC sp_executesql N'CREATE SCHEMA migration_test';
END
GO

-- ============================================================
-- DROP all tables in FK-dependency order (children before parents)
-- ============================================================
IF OBJECT_ID('migration_test.fk_child',            'U') IS NOT NULL DROP TABLE migration_test.fk_child;
IF OBJECT_ID('migration_test.fk_parent',           'U') IS NOT NULL DROP TABLE migration_test.fk_parent;
IF OBJECT_ID('migration_test.fk_grandparent',      'U') IS NOT NULL DROP TABLE migration_test.fk_grandparent;
IF OBJECT_ID('migration_test.orders',              'U') IS NOT NULL DROP TABLE migration_test.orders;
IF OBJECT_ID('migration_test.audit_log',           'U') IS NOT NULL DROP TABLE migration_test.audit_log;
IF OBJECT_ID('migration_test.products',            'U') IS NOT NULL DROP TABLE migration_test.products;
IF OBJECT_ID('migration_test.users',               'U') IS NOT NULL DROP TABLE migration_test.users;
IF OBJECT_ID('migration_test.self_ref',            'U') IS NOT NULL DROP TABLE migration_test.self_ref;
IF OBJECT_ID('migration_test.type_coverage',       'U') IS NOT NULL DROP TABLE migration_test.type_coverage;
IF OBJECT_ID('migration_test.numeric_edge_cases',  'U') IS NOT NULL DROP TABLE migration_test.numeric_edge_cases;
IF OBJECT_ID('migration_test.string_edge_cases',   'U') IS NOT NULL DROP TABLE migration_test.string_edge_cases;
IF OBJECT_ID('migration_test.datetime_edge_cases', 'U') IS NOT NULL DROP TABLE migration_test.datetime_edge_cases;
IF OBJECT_ID('migration_test.binary_edge_cases',   'U') IS NOT NULL DROP TABLE migration_test.binary_edge_cases;
IF OBJECT_ID('migration_test.null_edge_cases',     'U') IS NOT NULL DROP TABLE migration_test.null_edge_cases;
IF OBJECT_ID('migration_test.reserved_words_cols', 'U') IS NOT NULL DROP TABLE migration_test.reserved_words_cols;
IF OBJECT_ID('migration_test.composite_pk',        'U') IS NOT NULL DROP TABLE migration_test.composite_pk;
IF OBJECT_ID('migration_test.multi_unique',        'U') IS NOT NULL DROP TABLE migration_test.multi_unique;
IF OBJECT_ID('migration_test.empty_table',         'U') IS NOT NULL DROP TABLE migration_test.empty_table;
IF OBJECT_ID('migration_test.wide_table',          'U') IS NOT NULL DROP TABLE migration_test.wide_table;
IF OBJECT_ID('migration_test.batch_test',          'U') IS NOT NULL DROP TABLE migration_test.batch_test;
GO

-- ============================================================
-- SECTION 1: Original tables (users, products, orders, audit_log)
-- ============================================================
CREATE TABLE migration_test.users (
    id            INT IDENTITY(1,1) PRIMARY KEY,
    username      VARCHAR(50)   NOT NULL UNIQUE,
    email         VARCHAR(100)  NOT NULL,
    password_hash VARBINARY(MAX),
    created_at    DATETIME2     DEFAULT GETUTCDATE(),
    updated_at    DATETIME2     DEFAULT GETUTCDATE()
);

CREATE TABLE migration_test.products (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    name        VARCHAR(255)  NOT NULL,
    description TEXT,
    price       DECIMAL(10,2),
    image       VARBINARY(MAX),
    thumbnail   VARBINARY(MAX),
    created_at  DATETIME2     DEFAULT GETUTCDATE()
);

CREATE TABLE migration_test.orders (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    user_id     INT NOT NULL REFERENCES migration_test.users(id),
    product_id  INT NOT NULL REFERENCES migration_test.products(id),
    quantity    INT NOT NULL,
    total_price DECIMAL(10,2),
    order_date  DATETIME2     DEFAULT GETUTCDATE(),
    status      VARCHAR(50)   DEFAULT 'pending'
);

CREATE TABLE migration_test.audit_log (
    id               INT IDENTITY(1,1) PRIMARY KEY,
    table_name       VARCHAR(100),
    operation        VARCHAR(10),
    record_id        INT,
    changed_by       VARCHAR(100),
    change_data      VARBINARY(MAX),
    change_timestamp DATETIME2 DEFAULT GETUTCDATE()
);

INSERT INTO migration_test.users (username, email, password_hash) VALUES
    ('alice',   'alice@example.com',   CONVERT(VARBINARY(MAX), 'hashed_password_1')),
    ('bob',     'bob@example.com',     CONVERT(VARBINARY(MAX), 'hashed_password_2')),
    ('charlie', 'charlie@example.com', CONVERT(VARBINARY(MAX), 'hashed_password_3')),
    ('diana',   'diana@example.com',   CONVERT(VARBINARY(MAX), 'hashed_password_4'));

INSERT INTO migration_test.products (name, description, price, image, thumbnail) VALUES
    ('Laptop Pro',          'High-performance laptop',  1299.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_DATA_1'), CONVERT(VARBINARY(MAX), 'PNG_THUMB_1')),
    ('Wireless Mouse',      'Ergonomic wireless mouse',   29.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_DATA_2'), CONVERT(VARBINARY(MAX), 'PNG_THUMB_2')),
    ('USB-C Hub',           'Multi-port USB-C hub',       49.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_DATA_3'), CONVERT(VARBINARY(MAX), 'PNG_THUMB_3')),
    ('Mechanical Keyboard', 'RGB mechanical keyboard',   149.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_DATA_4'), CONVERT(VARBINARY(MAX), 'PNG_THUMB_4')),
    ('Monitor 4K',          '27-inch 4K monitor',        399.99, CONVERT(VARBINARY(MAX), 'PNG_IMAGE_DATA_5'), CONVERT(VARBINARY(MAX), 'PNG_THUMB_5'));

INSERT INTO migration_test.orders (user_id, product_id, quantity, total_price, status) VALUES
    (1, 1, 1, 1299.99, 'completed'), (1, 2, 2,   59.98, 'completed'),
    (2, 3, 1,   49.99, 'pending'),   (2, 4, 1,  149.99, 'shipped'),
    (3, 1, 1, 1299.99, 'processing'),(3, 5, 2,  799.98, 'pending'),
    (4, 2, 3,   89.97, 'completed'), (4, 4, 1,  149.99, 'completed');

INSERT INTO migration_test.audit_log (table_name, operation, record_id, changed_by, change_data) VALUES
    ('users',    'INSERT', 1, 'system', CONVERT(VARBINARY(MAX), '{"name":"Alice"}')),
    ('products', 'INSERT', 1, 'system', CONVERT(VARBINARY(MAX), '{"name":"Laptop Pro"}')),
    ('orders',   'INSERT', 1, 'system', CONVERT(VARBINARY(MAX), '{"status":"open"}')),
    ('orders',   'UPDATE', 1, 'system', CONVERT(VARBINARY(MAX), '{"status":"ordered"}'));
GO

-- ============================================================
-- SECTION 2: Type Coverage — one column per SQL Server type handled by MapDataType
-- ============================================================
CREATE TABLE migration_test.type_coverage (
    id                   INT IDENTITY(1,1) PRIMARY KEY,
    -- Integer types
    col_int              INT              NULL,
    col_bigint           BIGINT           NULL,
    col_smallint         SMALLINT         NULL,
    col_tinyint          TINYINT          NULL,
    -- Numeric types
    col_decimal_10_2     DECIMAL(10,2)    NULL,
    col_decimal_18_0     DECIMAL(18,0)    NULL,
    col_decimal_38_10    DECIMAL(38,10)   NULL,
    col_numeric_15_5     NUMERIC(15,5)    NULL,
    col_float            FLOAT            NULL,   -- maps to double precision / BINARY_DOUBLE
    col_float24          FLOAT(24)        NULL,   -- maps to real / BINARY_FLOAT (precision <=24)
    col_real             REAL             NULL,
    col_money            MONEY            NULL,
    col_smallmoney       SMALLMONEY       NULL,
    -- Character types
    col_char_10          CHAR(10)         NULL,
    col_nchar_10         NCHAR(10)        NULL,
    col_varchar_100      VARCHAR(100)     NULL,
    col_varchar_max      VARCHAR(MAX)     NULL,
    col_nvarchar_100     NVARCHAR(100)    NULL,
    col_nvarchar_max     NVARCHAR(MAX)    NULL,
    col_text             TEXT             NULL,
    col_ntext            NTEXT            NULL,
    -- Date/time types
    col_date             DATE             NULL,
    col_time_0           TIME(0)          NULL,
    col_time_7           TIME(7)          NULL,
    col_datetime         DATETIME         NULL,
    col_datetime2_0      DATETIME2(0)     NULL,
    col_datetime2_7      DATETIME2(7)     NULL,
    col_smalldatetime    SMALLDATETIME    NULL,
    col_datetimeoffset   DATETIMEOFFSET(7) NULL,
    -- Binary types
    col_binary_16        BINARY(16)       NULL,
    col_varbinary_100    VARBINARY(100)   NULL,
    col_varbinary_max    VARBINARY(MAX)   NULL,
    -- Special types
    col_bit              BIT              NULL,
    col_uniqueidentifier UNIQUEIDENTIFIER NULL,
    col_xml              XML              NULL,
    col_sql_variant      SQL_VARIANT      NULL
);

-- Row 1: representative values for every type
INSERT INTO migration_test.type_coverage (
    col_int, col_bigint, col_smallint, col_tinyint,
    col_decimal_10_2, col_decimal_18_0, col_decimal_38_10, col_numeric_15_5,
    col_float, col_float24, col_real, col_money, col_smallmoney,
    col_char_10, col_nchar_10, col_varchar_100, col_varchar_max,
    col_nvarchar_100, col_nvarchar_max, col_text, col_ntext,
    col_date, col_time_0, col_time_7,
    col_datetime, col_datetime2_0, col_datetime2_7, col_smalldatetime, col_datetimeoffset,
    col_binary_16, col_varbinary_100, col_varbinary_max,
    col_bit, col_uniqueidentifier, col_xml, col_sql_variant
) VALUES (
    42, 9876543210, 1000, 255,
    12345.67, 999999999999999999, CAST('1234567890.1234567890' AS DECIMAL(38,10)), 12345.12345,
    3.14159265358979, 3.14159, 2.71828, 1234567.8901, 9999.9999,
    'CHAR      ', N'NCHAR     ', 'varchar value', 'varchar(max) value — can be very long',
    N'nvarchar value', N'nvarchar(max) — 你好世界 مرحبا 😀', 'text column value', N'ntext column value',
    '2024-06-15', '14:30:00', '14:30:00.1234567',
    '2024-06-15 14:30:00.123', '2024-06-15 14:30:00', '2024-06-15 14:30:00.1234567',
    '2024-06-15 14:30:00', '2024-06-15 14:30:00.1234567 +02:00',
    0x0102030405060708090A0B0C0D0E0F10,
    0x48656C6C6F576F726C64,
    CONVERT(VARBINARY(MAX), 'binary max data'),
    1, NEWID(), N'<root><item id="1">test &amp; value</item></root>', CAST(42 AS SQL_VARIANT)
);

-- Row 2: all NULLs (verifies NULL round-trip for every type)
INSERT INTO migration_test.type_coverage (col_int) VALUES (NULL);
GO

-- ============================================================
-- SECTION 3: Numeric Edge Cases
-- ============================================================
CREATE TABLE migration_test.numeric_edge_cases (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    label        VARCHAR(60)    NOT NULL,
    col_int      INT            NULL,
    col_bigint   BIGINT         NULL,
    col_smallint SMALLINT       NULL,
    col_tinyint  TINYINT        NULL,
    col_decimal  DECIMAL(38,10) NULL,
    col_float    FLOAT          NULL,
    col_real     REAL           NULL,
    col_money    MONEY          NULL
);

INSERT INTO migration_test.numeric_edge_cases
    (label, col_int, col_bigint, col_smallint, col_tinyint, col_decimal, col_float, col_real, col_money)
VALUES
    ('int_max',              2147483647,  NULL,  NULL, NULL, NULL, NULL, NULL, NULL),
    ('int_min',             -2147483648,  NULL,  NULL, NULL, NULL, NULL, NULL, NULL),
    ('int_zero',                      0,  NULL,  NULL, NULL, NULL, NULL, NULL, NULL),
    ('bigint_max',               NULL,  9223372036854775807,  NULL, NULL, NULL, NULL, NULL, NULL),
    ('bigint_min',               NULL, -9223372036854775808,  NULL, NULL, NULL, NULL, NULL, NULL),
    ('smallint_max',             NULL,  NULL,  32767,    NULL, NULL, NULL, NULL, NULL),
    ('smallint_min',             NULL,  NULL, -32768,    NULL, NULL, NULL, NULL, NULL),
    ('tinyint_max',              NULL,  NULL,   NULL,     255, NULL, NULL, NULL, NULL),
    ('tinyint_zero',             NULL,  NULL,   NULL,       0, NULL, NULL, NULL, NULL),
    ('decimal_max_pos',          NULL,  NULL,   NULL, NULL, CAST('9999999999999999999999999999.9999999999' AS DECIMAL(38,10)), NULL, NULL, NULL),
    ('decimal_max_neg',          NULL,  NULL,   NULL, NULL, CAST('-9999999999999999999999999999.9999999999' AS DECIMAL(38,10)), NULL, NULL, NULL),
    ('decimal_zero',             NULL,  NULL,   NULL, NULL, 0.0000000000, NULL, NULL, NULL),
    ('decimal_tiny_fraction',    NULL,  NULL,   NULL, NULL, 0.0000000001, NULL, NULL, NULL),
    ('decimal_integer_like',     NULL,  NULL,   NULL, NULL, 1000000.0000000000, NULL, NULL, NULL),
    ('float_large_pos',          NULL,  NULL,   NULL, NULL, NULL, 1.7976931348623157E+308, NULL, NULL),
    ('float_large_neg',          NULL,  NULL,   NULL, NULL, NULL, -1.7976931348623157E+308, NULL, NULL),
    ('float_small_pos',          NULL,  NULL,   NULL, NULL, NULL, 2.2250738585072014E-308, NULL, NULL),
    ('float_zero',               NULL,  NULL,   NULL, NULL, NULL, 0.0, NULL, NULL),
    ('float_one',                NULL,  NULL,   NULL, NULL, NULL, 1.0, NULL, NULL),
    ('float_negative_one',       NULL,  NULL,   NULL, NULL, NULL, -1.0, NULL, NULL),
    ('real_max',                 NULL,  NULL,   NULL, NULL, NULL, NULL, 3.40282347E+38, NULL),
    ('real_min_positive',        NULL,  NULL,   NULL, NULL, NULL, NULL, 1.17549435E-38, NULL),
    ('money_max',                NULL,  NULL,   NULL, NULL, NULL, NULL, NULL, 922337203685477.5807),
    ('money_min',                NULL,  NULL,   NULL, NULL, NULL, NULL, NULL, -922337203685477.5808),
    ('money_zero',               NULL,  NULL,   NULL, NULL, NULL, NULL, NULL, 0.0000),
    ('all_null',                 NULL,  NULL,   NULL, NULL, NULL, NULL, NULL, NULL);
GO

-- ============================================================
-- SECTION 4: String Edge Cases
-- ============================================================
CREATE TABLE migration_test.string_edge_cases (
    id               INT IDENTITY(1,1) PRIMARY KEY,
    label            VARCHAR(80)   NOT NULL,
    col_varchar      VARCHAR(8000) NULL,
    col_nvarchar     NVARCHAR(4000) NULL,
    col_varchar_max  VARCHAR(MAX)  NULL,
    col_nvarchar_max NVARCHAR(MAX) NULL,
    col_char_10      CHAR(10)      NULL,
    col_text         TEXT          NULL
);

INSERT INTO migration_test.string_edge_cases
    (label, col_varchar, col_nvarchar, col_varchar_max, col_nvarchar_max, col_char_10, col_text)
VALUES
    -- Basic edge cases
    ('empty_string',        '',                N'',                '',                  N'',                  '          ', ''),
    ('single_space',        ' ',               N' ',               ' ',                 N' ',                 ' ',          ' '),
    ('single_quote',        'O''Brien''s',     N'O''Brien''s',     'it''s a ''test''',  N'it''s a ''test''',  'quote''    ', 'apostrophe''s'),
    ('double_quote',        'say "hello"',     N'say "hello"',     '"quoted string"',   N'"quoted"',          '"quote"   ', 'double "quotes"'),
    ('backslash',           'C:\path\to\file', N'C:\path\to\file', 'a\b\c\d',           N'a\b\c',             'back\     ', 'back\slash'),
    -- Control characters
    ('newline',             'line1' + CHAR(10) + 'line2',
                            N'line1' + NCHAR(10) + N'line2',
                            'a' + CHAR(13) + CHAR(10) + 'b',
                            N'CR' + NCHAR(13) + N'LF' + NCHAR(10) + N'end',
                            NULL, 'line' + CHAR(10) + 'break'),
    ('tab_char',            'col1' + CHAR(9) + 'col2',
                            N'col1' + NCHAR(9) + N'col2',
                            'a' + CHAR(9) + 'b' + CHAR(9) + 'c',
                            N'tab' + NCHAR(9) + N'sep',
                            NULL, NULL),
    -- Unicode
    ('unicode_cjk',         NULL, N'你好世界',         NULL, N'中文: 你好世界 — 日本語: こんにちは', NULL, NULL),
    ('unicode_arabic',      NULL, N'مرحبا بالعالم',   NULL, N'العربية: مرحبا',                    NULL, NULL),
    ('unicode_emoji',       NULL, N'😀🎉🚀💡🔥',      NULL, N'Hello 😀 World 🌍',                  NULL, NULL),
    ('unicode_diacritics',  NULL, N'Ñoño Ünïcödë',    NULL, N'Ñoño café naïve résumé',             NULL, NULL),
    ('unicode_rtl_mark',    NULL, N'LTR' + NCHAR(0x200F) + N'mixed', NULL, N'zero-width' + NCHAR(0x200B) + N'space', NULL, NULL),
    -- Injection / special patterns
    ('sql_injection',       '; DROP TABLE users; --',
                            N''' OR ''1''=''1',
                            'UNION SELECT * FROM sys.tables --',
                            N'"; DELETE FROM users; --',
                            NULL, '; DROP TABLE--'),
    ('xml_html_chars',      '<root>&amp;</root>',
                            N'<tag attr="x">text</tag>',
                            '<a href="x">link &amp; more</a>',
                            N'<!-- comment --> <![CDATA[data]]>',
                            NULL, '<root/>'),
    -- Length boundary
    ('varchar_4001_chars',  REPLICATE('A', 4001), NULL, REPLICATE('B', 8000), NULL, NULL, NULL),
    ('all_null',            NULL, NULL, NULL, NULL, NULL, NULL);

-- Very long string: forces VARCHAR(MAX) → CLOB/TEXT path in target DBs
DECLARE @longStr VARCHAR(MAX) = REPLICATE(CAST('Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor. ' AS VARCHAR(MAX)), 70);
INSERT INTO migration_test.string_edge_cases (label, col_varchar_max, col_nvarchar_max, col_text)
VALUES ('long_string_5600', @longStr, CAST(@longStr AS NVARCHAR(MAX)), @longStr);
GO

-- ============================================================
-- SECTION 5: Datetime Edge Cases
-- ============================================================
CREATE TABLE migration_test.datetime_edge_cases (
    id                   INT IDENTITY(1,1) PRIMARY KEY,
    label                VARCHAR(60)       NOT NULL,
    col_date             DATE              NULL,
    col_time_0           TIME(0)           NULL,
    col_time_7           TIME(7)           NULL,
    col_datetime         DATETIME          NULL,  -- precision ~3.33ms; min 1753-01-01
    col_datetime2_0      DATETIME2(0)      NULL,  -- min 0001-01-01
    col_datetime2_3      DATETIME2(3)      NULL,
    col_datetime2_7      DATETIME2(7)      NULL,  -- max precision 100ns
    col_smalldatetime    SMALLDATETIME     NULL,  -- 1-min precision; range 1900-2079
    col_datetimeoffset_0 DATETIMEOFFSET(0) NULL,
    col_datetimeoffset_7 DATETIMEOFFSET(7) NULL
);

INSERT INTO migration_test.datetime_edge_cases
    (label, col_date, col_time_0, col_time_7,
     col_datetime, col_datetime2_0, col_datetime2_3, col_datetime2_7,
     col_smalldatetime, col_datetimeoffset_0, col_datetimeoffset_7)
VALUES
    ('typical',
     '2024-06-15', '14:30:00', '14:30:00.1234567',
     '2024-06-15 14:30:00.123', '2024-06-15 14:30:00', '2024-06-15 14:30:00.123', '2024-06-15 14:30:00.1234567',
     '2024-06-15 14:30:00', '2024-06-15 14:30:00 +02:00', '2024-06-15 14:30:00.1234567 +05:30'),
    ('midnight',
     '2024-01-01', '00:00:00', '00:00:00.0000000',
     '2024-01-01 00:00:00.000', '2024-01-01 00:00:00', '2024-01-01 00:00:00.000', '2024-01-01 00:00:00.0000000',
     '2024-01-01 00:00:00', '2024-01-01 00:00:00 +00:00', '2024-01-01 00:00:00.0000000 +00:00'),
    ('end_of_day',
     '2024-12-31', '23:59:59', '23:59:59.9999999',
     '2024-12-31 23:59:59.997', '2024-12-31 23:59:59', '2024-12-31 23:59:59.999', '2024-12-31 23:59:59.9999999',
     '2024-12-31 23:59:00', '2024-12-31 23:59:59 +14:00', '2024-12-31 23:59:59.9999999 -12:00'),
    ('datetime_type_min',
     '1753-01-01', '00:00:00', '00:00:00.0000001',
     '1753-01-01 00:00:00.000',
     '0001-01-01 00:00:00', '0001-01-01 00:00:00.000', '0001-01-01 00:00:00.0000001',
     '1900-01-01 00:00:00', '0001-01-01 00:00:00 +00:00', '0001-01-01 00:00:00.0000000 +00:00'),
    ('datetime_type_max',
     '9999-12-31', '23:59:59', '23:59:59.9999999',
     '9999-12-31 23:59:59.997', '9999-12-31 23:59:59', '9999-12-31 23:59:59.997', '9999-12-31 23:59:59.9999999',
     '2079-06-06 23:59:00', '9999-12-31 23:59:59 +00:00', '9999-12-31 23:59:59.9999999 +00:00'),
    ('unix_epoch',
     '1970-01-01', '00:00:00', '00:00:00.0000000',
     '1970-01-01 00:00:00.000', '1970-01-01 00:00:00', '1970-01-01 00:00:00.000', '1970-01-01 00:00:00.0000000',
     '1970-01-01 00:00:00', '1970-01-01 00:00:00 +00:00', '1970-01-01 00:00:00.0000000 +00:00'),
    ('tz_max_positive',   NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     '2024-06-15 00:00:00 +14:00', '2024-06-15 12:00:00.1234567 +14:00'),
    ('tz_max_negative',   NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     '2024-06-15 00:00:00 -12:00', '2024-06-15 12:00:00.1234567 -12:00'),
    ('tz_utc',            NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     '2024-06-15 12:00:00 +00:00', '2024-06-15 12:00:00.0000000 +00:00'),
    ('all_null',          NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
GO

-- ============================================================
-- SECTION 6: Binary Edge Cases
-- ============================================================
CREATE TABLE migration_test.binary_edge_cases (
    id                INT IDENTITY(1,1) PRIMARY KEY,
    label             VARCHAR(60)   NOT NULL,
    col_binary_1      BINARY(1)     NULL,
    col_binary_16     BINARY(16)    NULL,
    col_varbinary_10  VARBINARY(10) NULL,
    col_varbinary_max VARBINARY(MAX) NULL,
    col_image         IMAGE         NULL   -- deprecated type, maps to BLOB
);

INSERT INTO migration_test.binary_edge_cases
    (label, col_binary_1, col_binary_16, col_varbinary_10, col_varbinary_max, col_image)
VALUES
    ('all_zeros',       0x00, 0x00000000000000000000000000000000, 0x0000000000, 0x0000000000000000, 0x00000000),
    ('all_ff',          0xFF, 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF, 0xFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFF),
    ('sequential',      0x01, 0x000102030405060708090A0B0C0D0E0F, 0x0102030405, 0x000102030405060708090A0B0C0D0E0F10, 0x010203),
    ('text_as_binary',  NULL, NULL, NULL, CONVERT(VARBINARY(MAX), 'Hello, World!'), CONVERT(VARBINARY(MAX), 'Image data')),
    ('png_magic',       NULL, NULL, NULL, 0x89504E470D0A1A0A0000000D49484452, NULL),   -- PNG header
    ('pdf_magic',       NULL, NULL, NULL, 0x255044462D312E34,                  NULL),   -- %PDF-1.4
    ('guid_bytes',      NULL, 0x6BA7B8109DAD11D180B400C04FD430C8, NULL, NULL, NULL),   -- UUID as raw bytes
    ('single_null_byte',0x00, NULL, 0x00, 0x00, NULL),
    ('all_null',        NULL, NULL, NULL, NULL, NULL);
GO

-- ============================================================
-- SECTION 7: NULL Edge Cases
-- Every nullable column tested with full row, all-NULL row, and mixed rows
-- ============================================================
CREATE TABLE migration_test.null_edge_cases (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    col_int      INT              NULL,
    col_bigint   BIGINT           NULL,
    col_decimal  DECIMAL(10,2)    NULL,
    col_float    FLOAT            NULL,
    col_varchar  VARCHAR(100)     NULL,
    col_nvarchar NVARCHAR(100)    NULL,
    col_text     TEXT             NULL,
    col_date     DATE             NULL,
    col_datetime DATETIME2        NULL,
    col_bit      BIT              NULL,
    col_binary   VARBINARY(MAX)   NULL,
    col_guid     UNIQUEIDENTIFIER NULL
);

INSERT INTO migration_test.null_edge_cases
    (col_int, col_bigint, col_decimal, col_float, col_varchar, col_nvarchar, col_text, col_date, col_datetime, col_bit, col_binary, col_guid)
VALUES
    -- All non-null
    (1,    100,  9.99,  3.14, 'hello',  N'world',    'text value', '2024-01-01', '2024-01-01 12:00:00', 1, 0x01020304, NEWID()),
    -- All null
    (NULL, NULL, NULL,  NULL, NULL,     NULL,         NULL,         NULL,         NULL,                  NULL, NULL,   NULL),
    -- Mixed set A: integers present, strings null
    (2,    -1,   0.00,  NULL, NULL,     NULL,         NULL,         '2000-02-29', NULL,                  0,    NULL,   NULL),
    -- Mixed set B: strings present, numerics null
    (NULL, NULL, NULL,  -0.0, 'empty?', N'unicode 日', NULL,         NULL,         '1970-01-01 00:00:00', NULL, 0xFF,  NULL),
    -- Zero / false values (distinct from NULL)
    (0,    0,    0.00,  0.0,  '',       N'',          '',           '1900-01-01', '1900-01-01 00:00:00', 0,    0x00,  NEWID()),
    -- Negative / boundary values
    (-1,  -9223372036854775808, -99999999.99, -1.0E+308, 'negative', N'负数', 'neg text', '1753-01-01', '0001-01-01 00:00:00', 1, 0xDEADBEEF, NEWID());
GO

-- ============================================================
-- SECTION 8: Reserved Word Column Names (quoted identifiers)
-- Tests that migrator correctly quotes identifiers on all target DBs
-- ============================================================
CREATE TABLE migration_test.reserved_words_cols (
    id       INT IDENTITY(1,1) PRIMARY KEY,
    [order]  INT           NULL,
    [group]  VARCHAR(50)   NULL,
    [select] VARCHAR(50)   NULL,
    [from]   VARCHAR(100)  NULL,
    [where]  VARCHAR(100)  NULL,
    [key]    VARCHAR(50)   NULL,
    [value]  NVARCHAR(MAX) NULL,
    [index]  INT           NULL,
    [date]   DATE          NULL,
    [type]   VARCHAR(50)   NULL,
    [status] VARCHAR(50)   NULL,
    [level]  INT           NULL,
    [name]   VARCHAR(100)  NULL,
    [user]   VARCHAR(50)   NULL,
    [table]  VARCHAR(50)   NULL
);

INSERT INTO migration_test.reserved_words_cols
    ([order],[group],[select],[from],[where],[key],[value],[index],[date],[type],[status],[level],[name],[user],[table])
VALUES
    (1, 'admin',   'SELECT 1', 'table_a', 'id = 1', 'key001', N'{"x":1}', 0, '2024-01-01', 'typeA', 'active',   1, 'Alice', 'dbo', 'users'),
    (2, 'editors', 'SELECT *', 'table_b', 'id > 0', 'key002', N'[1,2,3]', 1, '2024-06-15', 'typeB', 'inactive', 5, 'Bob',   'app', 'products'),
    (NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
GO

-- ============================================================
-- SECTION 9: Composite Primary Key (3-column PK)
-- ============================================================
CREATE TABLE migration_test.composite_pk (
    tenant_id    INT          NOT NULL,
    entity_type  VARCHAR(50)  NOT NULL,
    entity_id    BIGINT       NOT NULL,
    label        VARCHAR(100) NULL,
    payload      NVARCHAR(MAX) NULL,
    created_at   DATETIME2    DEFAULT GETUTCDATE(),
    CONSTRAINT PK_composite_pk PRIMARY KEY (tenant_id, entity_type, entity_id)
);

INSERT INTO migration_test.composite_pk (tenant_id, entity_type, entity_id, label, payload) VALUES
    (1, 'user',    100, 'Tenant1 User 100',     N'{"role":"admin"}'),
    (1, 'user',    101, 'Tenant1 User 101',     N'{"role":"viewer"}'),
    (1, 'product', 200, 'Tenant1 Product 200',  N'{"sku":"SKU-200"}'),
    (2, 'user',    100, 'Tenant2 User 100',     N'{"role":"editor"}'),  -- same entity_id, diff tenant
    (2, 'order',   300, 'Tenant2 Order 300',    NULL),
    (3, 'product', 200, 'Tenant3 Product 200',  N'{"sku":"SKU-200"}');  -- same as tenant1
GO

-- ============================================================
-- SECTION 10: Multiple UNIQUE Constraints on different columns
-- ============================================================
CREATE TABLE migration_test.multi_unique (
    id        INT IDENTITY(1,1) PRIMARY KEY,
    sku       VARCHAR(50)  NOT NULL,
    barcode   VARCHAR(50)  NOT NULL,
    serial_no VARCHAR(100) NULL,
    email     VARCHAR(100) NULL,
    label     VARCHAR(200) NULL,
    CONSTRAINT UQ_multi_unique_sku     UNIQUE (sku),
    CONSTRAINT UQ_multi_unique_barcode UNIQUE (barcode),
    CONSTRAINT UQ_multi_unique_serial  UNIQUE (serial_no),
    CONSTRAINT UQ_multi_unique_email   UNIQUE (email)
);

INSERT INTO migration_test.multi_unique (sku, barcode, serial_no, email, label) VALUES
    ('SKU-001', 'BAR-001', 'SN-001', 'prod1@example.com', 'Product 1'),
    ('SKU-002', 'BAR-002', 'SN-002', 'prod2@example.com', 'Product 2'),
    ('SKU-003', 'BAR-003', NULL,     NULL,                'Product 3 – no serial/email'),
    ('SKU-004', 'BAR-004', 'SN-004', 'prod4@example.com', 'Product 4');
GO

-- ============================================================
-- SECTION 11: 3-Level FK Chain (grandparent → parent → child)
-- ============================================================
CREATE TABLE migration_test.fk_grandparent (
    id   INT IDENTITY(1,1) PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    code VARCHAR(20)  NOT NULL UNIQUE
);

CREATE TABLE migration_test.fk_parent (
    id             INT IDENTITY(1,1) PRIMARY KEY,
    grandparent_id INT NOT NULL REFERENCES migration_test.fk_grandparent(id),
    name           VARCHAR(100) NOT NULL,
    description    VARCHAR(500) NULL
);

CREATE TABLE migration_test.fk_child (
    id         INT IDENTITY(1,1) PRIMARY KEY,
    parent_id  INT NOT NULL REFERENCES migration_test.fk_parent(id),
    name       VARCHAR(100)  NOT NULL,
    value      DECIMAL(10,2) NULL,
    created_at DATETIME2     DEFAULT GETUTCDATE()
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
GO

-- ============================================================
-- SECTION 12: Self-Referencing FK (category tree)
-- ============================================================
CREATE TABLE migration_test.self_ref (
    id        INT IDENTITY(1,1) PRIMARY KEY,
    parent_id INT          NULL REFERENCES migration_test.self_ref(id),
    name      VARCHAR(100) NOT NULL,
    depth     INT          NOT NULL DEFAULT 0,
    path      VARCHAR(500) NULL
);

-- Root nodes first (parent_id = NULL)
INSERT INTO migration_test.self_ref (parent_id, name, depth, path) VALUES
    (NULL, 'Electronics', 0, '/Electronics'),
    (NULL, 'Clothing',    0, '/Clothing'),
    (NULL, 'Books',       0, '/Books');

-- Level 1
INSERT INTO migration_test.self_ref (parent_id, name, depth, path) VALUES
    (1, 'Computers',  1, '/Electronics/Computers'),
    (1, 'Phones',     1, '/Electronics/Phones'),
    (1, 'Audio',      1, '/Electronics/Audio'),
    (2, 'Men',        1, '/Clothing/Men'),
    (2, 'Women',      1, '/Clothing/Women');

-- Level 2
INSERT INTO migration_test.self_ref (parent_id, name, depth, path) VALUES
    (4, 'Laptops',      2, '/Electronics/Computers/Laptops'),
    (4, 'Desktops',     2, '/Electronics/Computers/Desktops'),
    (5, 'Smartphones',  2, '/Electronics/Phones/Smartphones'),
    (7, 'Shirts',       2, '/Clothing/Men/Shirts'),
    (8, 'Dresses',      2, '/Clothing/Women/Dresses');
GO

-- ============================================================
-- SECTION 13: Empty Table (Schema-Only migration test)
-- ============================================================
CREATE TABLE migration_test.empty_table (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    name        VARCHAR(100)  NOT NULL,
    description NVARCHAR(MAX) NULL,
    value       DECIMAL(18,6) NULL,
    active      BIT           NOT NULL DEFAULT 1,
    created_at  DATETIME2     DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_empty_table_name UNIQUE (name)
);
-- Intentionally no rows — used for Schema Only migration tests
GO

-- ============================================================
-- SECTION 14: Wide Table (30 columns — tests column handling at scale)
-- ============================================================
CREATE TABLE migration_test.wide_table (
    id      INT IDENTITY(1,1) PRIMARY KEY,
    col_01  VARCHAR(50)    NULL, col_02  VARCHAR(50)    NULL,
    col_03  INT            NULL, col_04  INT            NULL,
    col_05  DECIMAL(10,2)  NULL, col_06  DECIMAL(10,2)  NULL,
    col_07  DATETIME2      NULL, col_08  DATETIME2      NULL,
    col_09  BIT            NULL, col_10  BIT            NULL,
    col_11  NVARCHAR(200)  NULL, col_12  NVARCHAR(200)  NULL,
    col_13  BIGINT         NULL, col_14  BIGINT         NULL,
    col_15  FLOAT          NULL, col_16  FLOAT          NULL,
    col_17  DATE           NULL, col_18  DATE           NULL,
    col_19  TIME(3)        NULL, col_20  TIME(3)        NULL,
    col_21  VARBINARY(MAX) NULL, col_22  UNIQUEIDENTIFIER NULL,
    col_23  VARCHAR(MAX)   NULL, col_24  SMALLINT       NULL,
    col_25  TINYINT        NULL, col_26  CHAR(5)        NULL,
    col_27  MONEY          NULL, col_28  REAL           NULL,
    col_29  NCHAR(10)      NULL, col_30  XML            NULL
);

INSERT INTO migration_test.wide_table
    (col_01,col_02,col_03,col_04,col_05,col_06,col_07,col_08,col_09,col_10,
     col_11,col_12,col_13,col_14,col_15,col_16,col_17,col_18,col_19,col_20,
     col_21,col_22,col_23,col_24,col_25,col_26,col_27,col_28,col_29,col_30)
VALUES
    ('alpha','beta',1,2,1.11,2.22,'2024-01-01','2024-06-15',1,0,
     N'unicode 日本語',N'emoji 😀',9999999,-9999999,1.1,2.2,'2024-01-01','2024-06-15','10:30:00.123','23:59:59.999',
     0xDEADBEEF,NEWID(),REPLICATE('X',1000),32000,200,'ABCDE',9999.9999,3.14,N'NCHARTEST',
     N'<r><v>1</v></r>'),
    -- All-NULL row
    (NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
     NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
     NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
GO

-- ============================================================
-- SECTION 15: Batch Test (1200 rows — verifies 1000-row batch processing)
-- ============================================================
CREATE TABLE migration_test.batch_test (
    id         INT IDENTITY(1,1) PRIMARY KEY,
    seq_num    INT           NOT NULL,
    label      VARCHAR(50)   NOT NULL,
    value_int  INT           NULL,
    value_dec  DECIMAL(12,4) NULL,
    value_str  VARCHAR(100)  NULL,
    created_at DATETIME2     DEFAULT GETUTCDATE()
);

;WITH nums AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM nums WHERE n < 1200
)
INSERT INTO migration_test.batch_test (seq_num, label, value_int, value_dec, value_str)
SELECT
    n,
    'Row_' + RIGHT('0000' + CAST(n AS VARCHAR(4)), 4),
    n % 1000,
    CAST(n AS DECIMAL(12,4)) * 3.14159,
    CASE n % 5
        WHEN 0 THEN 'fizzbuzz' WHEN 1 THEN 'one'
        WHEN 2 THEN 'two'      WHEN 3 THEN 'three'
        ELSE 'four'
    END
FROM nums
OPTION (MAXRECURSION 0);
GO
