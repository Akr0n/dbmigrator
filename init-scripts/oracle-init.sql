-- Oracle Database Free Initialization Script
-- Comprehensive edge-case test data for migration testing
-- Covers: all mapped types (NUMBER variants, VARCHAR2/NVARCHAR2, CLOB/NCLOB/BLOB,
--         DATE/TIMESTAMP/INTERVAL, RAW, BINARY_FLOAT/BINARY_DOUBLE, XMLTYPE),
--         numeric/string/datetime/binary edge cases, NULL handling,
--         reserved-word column names, composite PK, multiple UNIQUE constraints,
--         3-level FK chain, self-referencing FK, empty table, wide table, batch test (>1000 rows)
-- Idempotent: safe to re-run (uses BEGIN/EXCEPTION blocks for conditional drops)
-- NOTE: Oracle treats '' (empty string) as NULL — this is a key migration edge case!

ALTER SESSION SET CONTAINER=FREEPDB1;

CREATE USER migration_test IDENTIFIED BY "oraclepass123" QUOTA UNLIMITED ON USERS;
GRANT CONNECT, RESOURCE TO migration_test;
GRANT CREATE TABLE, CREATE SEQUENCE TO migration_test;

CONNECT migration_test/oraclepass123@//localhost/FREEPDB1;

-- ============================================================
-- DROP all objects in FK-dependency order (CASCADE CONSTRAINTS handles FKs)
-- Uses BEGIN/EXCEPTION to silently skip if object does not exist
-- ============================================================
BEGIN EXECUTE IMMEDIATE 'DROP TABLE fk_child            CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE fk_parent           CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE fk_grandparent      CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE orders              CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE audit_log           CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE products            CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE users               CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE self_ref            CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE type_coverage       CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE numeric_edge_cases  CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE string_edge_cases   CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE datetime_edge_cases CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE binary_edge_cases   CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE null_edge_cases     CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE reserved_words_cols CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE composite_pk        CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE multi_unique        CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE empty_table         CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE wide_table          CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP TABLE batch_test          CASCADE CONSTRAINTS'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;
/

-- Drop all sequences
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE users_seq';              EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE products_seq';           EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE orders_seq';             EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE audit_log_seq';          EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE type_coverage_seq';      EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE numeric_ec_seq';         EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE string_ec_seq';          EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE datetime_ec_seq';        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE binary_ec_seq';          EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE null_ec_seq';            EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE reserved_seq';           EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE composite_pk_seq';       EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE multi_unique_seq';       EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE fk_grandparent_seq';     EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE fk_parent_seq';          EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE fk_child_seq';           EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE self_ref_seq';           EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE empty_table_seq';        EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE wide_table_seq';         EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE batch_test_seq';         EXCEPTION WHEN OTHERS THEN IF SQLCODE != -2289 THEN RAISE; END IF; END;
/

-- ============================================================
-- SECTION 1: Original tables (users, products, orders, audit_log)
-- ============================================================
CREATE TABLE users (
    id            NUMBER PRIMARY KEY,
    username      VARCHAR2(50)  NOT NULL UNIQUE,
    email         VARCHAR2(100) NOT NULL,
    password_hash BLOB,
    created_at    TIMESTAMP DEFAULT SYSDATE,
    updated_at    TIMESTAMP DEFAULT SYSDATE
);
CREATE SEQUENCE users_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE products (
    id          NUMBER PRIMARY KEY,
    name        VARCHAR2(255) NOT NULL,
    description CLOB,
    price       NUMBER(10,2),
    image       BLOB,
    thumbnail   BLOB,
    created_at  TIMESTAMP DEFAULT SYSDATE
);
CREATE SEQUENCE products_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE orders (
    id          NUMBER PRIMARY KEY,
    user_id     NUMBER NOT NULL REFERENCES users(id),
    product_id  NUMBER NOT NULL REFERENCES products(id),
    quantity    NUMBER NOT NULL,
    total_price NUMBER(10,2),
    order_date  TIMESTAMP DEFAULT SYSDATE,
    status      VARCHAR2(50) DEFAULT 'pending'
);
CREATE SEQUENCE orders_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE audit_log (
    id               NUMBER PRIMARY KEY,
    table_name       VARCHAR2(100),
    operation        VARCHAR2(10),
    record_id        NUMBER,
    changed_by       VARCHAR2(100),
    change_data      BLOB,
    change_timestamp TIMESTAMP DEFAULT SYSDATE
);
CREATE SEQUENCE audit_log_seq START WITH 1 INCREMENT BY 1;

INSERT INTO users (id,username,email,password_hash) VALUES (users_seq.NEXTVAL,'alice','alice@example.com',UTL_RAW.CAST_TO_RAW('hashed_password_1'));
INSERT INTO users (id,username,email,password_hash) VALUES (users_seq.NEXTVAL,'bob','bob@example.com',UTL_RAW.CAST_TO_RAW('hashed_password_2'));
INSERT INTO users (id,username,email,password_hash) VALUES (users_seq.NEXTVAL,'charlie','charlie@example.com',UTL_RAW.CAST_TO_RAW('hashed_password_3'));
INSERT INTO users (id,username,email,password_hash) VALUES (users_seq.NEXTVAL,'diana','diana@example.com',UTL_RAW.CAST_TO_RAW('hashed_password_4'));

INSERT INTO products (id,name,description,price,image,thumbnail) VALUES (products_seq.NEXTVAL,'Laptop Pro','High-performance laptop',1299.99,UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_1'),UTL_RAW.CAST_TO_RAW('PNG_THUMB_1'));
INSERT INTO products (id,name,description,price,image,thumbnail) VALUES (products_seq.NEXTVAL,'Wireless Mouse','Ergonomic wireless mouse',29.99,UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_2'),UTL_RAW.CAST_TO_RAW('PNG_THUMB_2'));
INSERT INTO products (id,name,description,price,image,thumbnail) VALUES (products_seq.NEXTVAL,'USB-C Hub','Multi-port USB-C hub',49.99,UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_3'),UTL_RAW.CAST_TO_RAW('PNG_THUMB_3'));
INSERT INTO products (id,name,description,price,image,thumbnail) VALUES (products_seq.NEXTVAL,'Mechanical Keyboard','RGB mechanical keyboard',149.99,UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_4'),UTL_RAW.CAST_TO_RAW('PNG_THUMB_4'));
INSERT INTO products (id,name,description,price,image,thumbnail) VALUES (products_seq.NEXTVAL,'Monitor 4K','27-inch 4K monitor',399.99,UTL_RAW.CAST_TO_RAW('PNG_IMAGE_DATA_5'),UTL_RAW.CAST_TO_RAW('PNG_THUMB_5'));

INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,1,1,1,1299.99,'completed');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,1,2,2,59.98,'completed');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,2,3,1,49.99,'pending');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,2,4,1,149.99,'shipped');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,3,1,1,1299.99,'processing');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,3,5,2,799.98,'pending');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,4,2,3,89.97,'completed');
INSERT INTO orders (id,user_id,product_id,quantity,total_price,status) VALUES (orders_seq.NEXTVAL,4,4,1,149.99,'completed');

INSERT INTO audit_log (id,table_name,operation,record_id,changed_by,change_data) VALUES (audit_log_seq.NEXTVAL,'users','INSERT',1,'system',UTL_RAW.CAST_TO_RAW('{"name":"Alice"}'));
INSERT INTO audit_log (id,table_name,operation,record_id,changed_by,change_data) VALUES (audit_log_seq.NEXTVAL,'products','INSERT',1,'system',UTL_RAW.CAST_TO_RAW('{"name":"Laptop Pro"}'));
INSERT INTO audit_log (id,table_name,operation,record_id,changed_by,change_data) VALUES (audit_log_seq.NEXTVAL,'orders','INSERT',1,'system',UTL_RAW.CAST_TO_RAW('{"status":"open"}'));
INSERT INTO audit_log (id,table_name,operation,record_id,changed_by,change_data) VALUES (audit_log_seq.NEXTVAL,'orders','UPDATE',1,'system',UTL_RAW.CAST_TO_RAW('{"status":"ordered"}'));
COMMIT;

-- ============================================================
-- SECTION 2: Type Coverage — one column per Oracle type handled by MapDataType
-- ============================================================
CREATE TABLE type_coverage (
    id                    NUMBER PRIMARY KEY,
    -- NUMBER variants (int-like, controlled by precision/scale)
    col_number_10         NUMBER(10)       NULL,   -- → INT in MSSQL, INTEGER in PG
    col_number_19         NUMBER(19)       NULL,   -- → BIGINT
    col_number_5          NUMBER(5)        NULL,   -- → SMALLINT
    col_number_3          NUMBER(3)        NULL,   -- → TINYINT
    col_number_10_2       NUMBER(10,2)     NULL,   -- → DECIMAL(10,2)
    col_number_38_10      NUMBER(38,10)    NULL,   -- high precision decimal
    col_number_no_prec    NUMBER           NULL,   -- → DECIMAL(18,2) in MSSQL, NUMERIC in PG
    col_integer           INTEGER          NULL,   -- Oracle INTEGER = NUMBER(38)
    -- Floating point (Oracle-native, no direct SQL Server equivalent)
    col_binary_float      BINARY_FLOAT     NULL,   -- → REAL in MSSQL, REAL in PG
    col_binary_double     BINARY_DOUBLE    NULL,   -- → FLOAT in MSSQL, DOUBLE PRECISION in PG
    col_float_126         FLOAT(126)       NULL,   -- → FLOAT/BINARY_DOUBLE (precision >24)
    col_float_24          FLOAT(24)        NULL,   -- → REAL/BINARY_FLOAT (precision <=24)
    -- Character types (Oracle max: VARCHAR2=4000, NVARCHAR2=2000, CHAR=2000, NCHAR=1000)
    col_varchar2_100      VARCHAR2(100)    NULL,
    col_varchar2_4000     VARCHAR2(4000)   NULL,   -- max VARCHAR2
    col_nvarchar2_100     NVARCHAR2(100)   NULL,
    col_nvarchar2_2000    NVARCHAR2(2000)  NULL,   -- max NVARCHAR2
    col_char_10           CHAR(10)         NULL,
    col_nchar_10          NCHAR(10)        NULL,
    col_clob              CLOB             NULL,   -- unlimited text
    col_nclob             NCLOB            NULL,   -- unlimited unicode text
    -- Date/time types
    col_date              DATE             NULL,   -- NOTE: Oracle DATE stores date+time (unlike SQL date!)
    col_timestamp_0       TIMESTAMP(0)     NULL,
    col_timestamp_6       TIMESTAMP(6)     NULL,
    col_timestamp_9       TIMESTAMP(9)     NULL,   -- max Oracle precision
    col_timestamp_tz      TIMESTAMP(6) WITH TIME ZONE NULL,
    col_timestamp_ltz     TIMESTAMP(6) WITH LOCAL TIME ZONE NULL,
    -- Interval types (Oracle-native)
    col_interval_ym       INTERVAL YEAR TO MONTH NULL,
    col_interval_ds       INTERVAL DAY TO SECOND NULL,
    -- Binary types
    col_raw_16            RAW(16)          NULL,   -- fixed binary up to 2000
    col_raw_2000          RAW(2000)        NULL,   -- max RAW
    col_blob              BLOB             NULL,   -- unlimited binary
    -- Special
    col_xmltype           XMLTYPE          NULL
);
CREATE SEQUENCE type_coverage_seq START WITH 1 INCREMENT BY 1;

-- Row 1: representative values for every type
INSERT INTO type_coverage (
    id, col_number_10, col_number_19, col_number_5, col_number_3,
    col_number_10_2, col_number_38_10, col_number_no_prec, col_integer,
    col_binary_float, col_binary_double, col_float_126, col_float_24,
    col_varchar2_100, col_varchar2_4000, col_nvarchar2_100, col_nvarchar2_2000,
    col_char_10, col_nchar_10, col_clob, col_nclob,
    col_date, col_timestamp_0, col_timestamp_6, col_timestamp_9,
    col_timestamp_tz, col_timestamp_ltz,
    col_interval_ym, col_interval_ds,
    col_raw_16, col_raw_2000, col_blob,
    col_xmltype
) VALUES (
    type_coverage_seq.NEXTVAL,
    42, 9876543210, 1000, 127,
    12345.67, 1234567890.1234567890, 999999.99, 12345678901234567890,
    3.14159265e0f, 3.14159265358979e0, 1.7976931348623157e+308, 3.40282347e+38f,
    'varchar2 value', RPAD('varchar2 4000 value', 100, ' '),
    N'nvarchar2 value', N'nvarchar2 unicode: 你好世界 مرحبا',
    'CHAR      ', N'NCHAR     ',
    'CLOB value — can be unlimited length text. ' || RPAD('x', 100, 'y'),
    N'NCLOB unicode value: 日本語テスト 中文 العربية',
    TO_DATE('2024-06-15 14:30:00', 'YYYY-MM-DD HH24:MI:SS'),
    TIMESTAMP '2024-06-15 14:30:00',
    TIMESTAMP '2024-06-15 14:30:00.123456',
    TIMESTAMP '2024-06-15 14:30:00.123456789',
    TIMESTAMP '2024-06-15 14:30:00.123456 +02:00',
    TIMESTAMP '2024-06-15 14:30:00.123456',
    INTERVAL '1-6' YEAR TO MONTH,
    INTERVAL '3 12:30:45.678' DAY TO SECOND,
    HEXTORAW('0102030405060708090A0B0C0D0E0F10'),
    HEXTORAW('48656C6C6F576F726C64'),
    UTL_RAW.CAST_TO_RAW('Binary BLOB data'),
    XMLTYPE('<root><item id="1">test &amp; value</item></root>')
);

-- Row 2: all NULLs (except PK)
INSERT INTO type_coverage (id, col_number_10) VALUES (type_coverage_seq.NEXTVAL, NULL);
COMMIT;

-- ============================================================
-- SECTION 3: Numeric Edge Cases
-- ============================================================
CREATE TABLE numeric_edge_cases (
    id           NUMBER PRIMARY KEY,
    label        VARCHAR2(60)  NOT NULL,
    col_number   NUMBER(38,10) NULL,
    col_int_like NUMBER(10)    NULL,
    col_bigint_like NUMBER(19) NULL,
    col_bf       BINARY_FLOAT  NULL,
    col_bd       BINARY_DOUBLE NULL
);
CREATE SEQUENCE numeric_ec_seq START WITH 1 INCREMENT BY 1;

INSERT ALL
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'number_max_prec',9999999999999999999999999999.9999999999,NULL,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'number_max_neg',-9999999999999999999999999999.9999999999,NULL,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'number_zero',0,NULL,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'number_tiny_frac',0.0000000001,NULL,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'number_integer_like',1000000.0000000000,NULL,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'int_like_max',NULL,2147483647,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'int_like_min',NULL,-2147483648,NULL,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'bigint_like_max',NULL,NULL,9223372036854775807,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'bigint_like_min',NULL,NULL,-9223372036854775808,NULL,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_float_max',NULL,NULL,NULL,3.40282347e+38f,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_float_min',NULL,NULL,NULL,1.17549435e-38f,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_float_zero',NULL,NULL,NULL,0.0f,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_double_max',NULL,NULL,NULL,NULL,1.7976931348623157e+308)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_double_min',NULL,NULL,NULL,NULL,2.2250738585072014e-308)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_float_nan',NULL,NULL,NULL,BINARY_FLOAT_NAN,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_float_inf',NULL,NULL,NULL,BINARY_FLOAT_INFINITY,NULL)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'binary_double_nan',NULL,NULL,NULL,NULL,BINARY_DOUBLE_NAN)
    INTO numeric_edge_cases VALUES (numeric_ec_seq.NEXTVAL,'all_null',NULL,NULL,NULL,NULL,NULL)
SELECT 1 FROM DUAL;
COMMIT;

-- ============================================================
-- SECTION 4: String Edge Cases
-- NOTE: In Oracle '' (empty string) = NULL — this is tested explicitly
-- ============================================================
CREATE TABLE string_edge_cases (
    id            NUMBER PRIMARY KEY,
    label         VARCHAR2(80)   NOT NULL,
    col_varchar2  VARCHAR2(4000) NULL,
    col_nvarchar2 NVARCHAR2(2000) NULL,
    col_clob      CLOB           NULL,
    col_nclob     NCLOB          NULL,
    col_char_10   CHAR(10)       NULL
);
CREATE SEQUENCE string_ec_seq START WITH 1 INCREMENT BY 1;

INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'single_quote','O''Brien''s','O''Brien''s','apostrophe''s in text','apostrophe''s','quote''    ');
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'double_quote','say "hello"',N'say "hello"','double "quotes"',N'"quoted"','"quote"   ');
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'backslash','C:\path\to\file',N'C:\path\to\file','back\slash',N'back\slash','back\     ');
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'newline','line1' || CHR(10) || 'line2',N'line1' || CHR(10) || N'line2','a' || CHR(13) || CHR(10) || 'b',N'CR' || CHR(13) || N'LF',NULL);
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'tab_char','col1' || CHR(9) || 'col2',N'col1' || CHR(9) || N'col2','a' || CHR(9) || 'b',N'tab' || CHR(9) || N'sep',NULL);
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'unicode_cjk',NULL,N'你好世界',NULL,N'中文: 你好世界 — 日本語: こんにちは',NULL);
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'unicode_arabic',NULL,N'مرحبا بالعالم',NULL,N'العربية: مرحبا',NULL);
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'unicode_diacritics',NULL,N'Ñoño Ünïcödë',NULL,N'Ñoño café naïve résumé',NULL);
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'sql_injection','; DROP TABLE users; --',N''' OR ''1''=''1','UNION SELECT * FROM dual --',N'"; DELETE FROM dual --',NULL);
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_nclob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'xml_html_chars','<root>&amp;</root>',N'<tag attr="x">text</tag>','<a href="x">link &amp; more</a>',N'<xml>test</xml>',NULL);
-- VARCHAR2 at max (4000): tests VARCHAR2 → CLOB boundary
INSERT INTO string_edge_cases (id,label,col_varchar2,col_clob)
VALUES (string_ec_seq.NEXTVAL,'varchar2_4000_max',RPAD('X',4000,'A'),RPAD('Y',5000,'B'));
-- Oracle empty string = NULL: inserting '' stores NULL — this is the key Oracle edge case
INSERT INTO string_edge_cases (id,label,col_varchar2,col_nvarchar2,col_clob,col_char_10)
VALUES (string_ec_seq.NEXTVAL,'empty_string_is_null','','',NULL,'          ');
-- Long CLOB (>4000 chars, must be CLOB)
DECLARE
    v_clob CLOB := RPAD('Lorem ipsum dolor sit amet, consectetur adipiscing elit. ', 5600, 'x');
BEGIN
    INSERT INTO string_edge_cases (id,label,col_clob,col_nclob)
    VALUES (string_ec_seq.NEXTVAL,'long_clob_5600',v_clob,v_clob);
END;
/
COMMIT;

-- ============================================================
-- SECTION 5: Datetime Edge Cases
-- NOTE: Oracle DATE type stores date AND time (unlike SQL Server/PG DATE)
-- ============================================================
CREATE TABLE datetime_edge_cases (
    id               NUMBER PRIMARY KEY,
    label            VARCHAR2(60)   NOT NULL,
    col_date         DATE           NULL,   -- stores date+time in Oracle!
    col_timestamp_0  TIMESTAMP(0)   NULL,
    col_timestamp_6  TIMESTAMP(6)   NULL,
    col_timestamp_9  TIMESTAMP(9)   NULL,
    col_ts_tz        TIMESTAMP(6) WITH TIME ZONE       NULL,
    col_ts_ltz       TIMESTAMP(6) WITH LOCAL TIME ZONE NULL,
    col_interval_ym  INTERVAL YEAR TO MONTH  NULL,
    col_interval_ds  INTERVAL DAY TO SECOND  NULL
);
CREATE SEQUENCE datetime_ec_seq START WITH 1 INCREMENT BY 1;

INSERT ALL
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'typical',
        TO_DATE('2024-06-15 14:30:00','YYYY-MM-DD HH24:MI:SS'),
        TIMESTAMP '2024-06-15 14:30:00',
        TIMESTAMP '2024-06-15 14:30:00.123456',
        TIMESTAMP '2024-06-15 14:30:00.123456789',
        TIMESTAMP '2024-06-15 14:30:00.123456 +02:00',
        TIMESTAMP '2024-06-15 14:30:00.123456',
        INTERVAL '1-6' YEAR TO MONTH,
        INTERVAL '3 12:30:45.678' DAY TO SECOND)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'midnight',
        TO_DATE('2024-01-01 00:00:00','YYYY-MM-DD HH24:MI:SS'),
        TIMESTAMP '2024-01-01 00:00:00',
        TIMESTAMP '2024-01-01 00:00:00.000000',
        TIMESTAMP '2024-01-01 00:00:00.000000000',
        TIMESTAMP '2024-01-01 00:00:00.000000 +00:00',
        TIMESTAMP '2024-01-01 00:00:00.000000',
        INTERVAL '0-0' YEAR TO MONTH,
        INTERVAL '0 00:00:00.000' DAY TO SECOND)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'end_of_day',
        TO_DATE('2024-12-31 23:59:59','YYYY-MM-DD HH24:MI:SS'),
        TIMESTAMP '2024-12-31 23:59:59',
        TIMESTAMP '2024-12-31 23:59:59.999999',
        TIMESTAMP '2024-12-31 23:59:59.999999999',
        TIMESTAMP '2024-12-31 23:59:59.999999 +14:00',
        TIMESTAMP '2024-12-31 23:59:59.999999',
        INTERVAL '99-11' YEAR TO MONTH,
        INTERVAL '99 23:59:59.999999999' DAY TO SECOND)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'oracle_date_has_time',
        TO_DATE('2024-06-15 08:45:30','YYYY-MM-DD HH24:MI:SS'),  -- Oracle DATE includes time!
        NULL,NULL,NULL,NULL,NULL,NULL,NULL)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'unix_epoch',
        TO_DATE('1970-01-01 00:00:00','YYYY-MM-DD HH24:MI:SS'),
        TIMESTAMP '1970-01-01 00:00:00',
        TIMESTAMP '1970-01-01 00:00:00.000000',
        TIMESTAMP '1970-01-01 00:00:00.000000000',
        TIMESTAMP '1970-01-01 00:00:00.000000 +00:00',
        TIMESTAMP '1970-01-01 00:00:00.000000',
        INTERVAL '0-0' YEAR TO MONTH,
        INTERVAL '0 00:00:00.000' DAY TO SECOND)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'tz_positive_max',
        NULL,NULL,NULL,NULL,
        TIMESTAMP '2024-06-15 12:00:00.000000 +14:00',
        NULL,NULL,NULL)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'tz_negative_max',
        NULL,NULL,NULL,NULL,
        TIMESTAMP '2024-06-15 12:00:00.000000 -12:00',
        NULL,NULL,NULL)
    INTO datetime_edge_cases VALUES (datetime_ec_seq.NEXTVAL,'all_null',
        NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
SELECT 1 FROM DUAL;
COMMIT;

-- ============================================================
-- SECTION 6: Binary Edge Cases
-- ============================================================
CREATE TABLE binary_edge_cases (
    id            NUMBER PRIMARY KEY,
    label         VARCHAR2(60) NOT NULL,
    col_raw_16    RAW(16)      NULL,
    col_raw_2000  RAW(2000)    NULL,
    col_blob      BLOB         NULL
);
CREATE SEQUENCE binary_ec_seq START WITH 1 INCREMENT BY 1;

INSERT ALL
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'all_zeros',HEXTORAW('00000000000000000000000000000000'),HEXTORAW('0000000000000000'),HEXTORAW('0000000000000000'))
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'all_ff',HEXTORAW('FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF'),HEXTORAW('FFFFFFFFFFFFFFFF'),HEXTORAW('FFFFFFFFFFFFFFFF'))
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'sequential',HEXTORAW('000102030405060708090A0B0C0D0E0F'),HEXTORAW('0102030405060708090A0B'),NULL)
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'png_magic',NULL,NULL,HEXTORAW('89504E470D0A1A0A0000000D49484452'))
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'pdf_magic',NULL,NULL,HEXTORAW('255044462D312E34'))
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'guid_raw16',HEXTORAW('6BA7B8109DAD11D180B400C04FD430C8'),NULL,NULL)
    INTO binary_edge_cases VALUES (binary_ec_seq.NEXTVAL,'null_all',NULL,NULL,NULL)
SELECT 1 FROM DUAL;

-- Text as BLOB (UTL_RAW conversion)
INSERT INTO binary_edge_cases (id,label,col_blob)
VALUES (binary_ec_seq.NEXTVAL,'text_as_blob',UTL_RAW.CAST_TO_RAW('Hello, World!'));
COMMIT;

-- ============================================================
-- SECTION 7: NULL Edge Cases
-- NOTE: '' (empty string) in Oracle is NULL — included to demonstrate behavior
-- ============================================================
CREATE TABLE null_edge_cases (
    id           NUMBER PRIMARY KEY,
    col_number   NUMBER(10,2)   NULL,
    col_varchar2 VARCHAR2(100)  NULL,
    col_nvarchar2 NVARCHAR2(100) NULL,
    col_clob     CLOB           NULL,
    col_date     DATE           NULL,
    col_timestamp TIMESTAMP     NULL,
    col_raw      RAW(100)       NULL,
    col_blob     BLOB           NULL
);
CREATE SEQUENCE null_ec_seq START WITH 1 INCREMENT BY 1;

INSERT INTO null_edge_cases VALUES (null_ec_seq.NEXTVAL,9.99,'hello',N'world','text value',SYSDATE,SYSTIMESTAMP,HEXTORAW('01020304'),UTL_RAW.CAST_TO_RAW('blob data'));
INSERT INTO null_edge_cases VALUES (null_ec_seq.NEXTVAL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO null_edge_cases VALUES (null_ec_seq.NEXTVAL,0.00,'','',NULL,TO_DATE('2000-02-29','YYYY-MM-DD'),NULL,NULL,NULL);   -- '' = NULL in Oracle
INSERT INTO null_edge_cases VALUES (null_ec_seq.NEXTVAL,-99.99,NULL,N'unicode 日本語',NULL,NULL,TIMESTAMP '1970-01-01 00:00:00',HEXTORAW('FF'),NULL);
INSERT INTO null_edge_cases VALUES (null_ec_seq.NEXTVAL,0,NULL,NULL,'',TO_DATE('1900-01-01','YYYY-MM-DD'),TIMESTAMP '1900-01-01 00:00:00',HEXTORAW('00'),HEXTORAW('00'));
COMMIT;

-- ============================================================
-- SECTION 8: Reserved Word Column Names (quoted identifiers)
-- ============================================================
CREATE TABLE reserved_words_cols (
    id       NUMBER PRIMARY KEY,
    "ORDER"  NUMBER         NULL,
    "GROUP"  VARCHAR2(50)   NULL,
    "SELECT" VARCHAR2(50)   NULL,
    "FROM"   VARCHAR2(100)  NULL,
    "WHERE"  VARCHAR2(100)  NULL,
    "KEY"    VARCHAR2(50)   NULL,
    "VALUE"  CLOB           NULL,
    "INDEX"  NUMBER         NULL,
    "DATE"   DATE           NULL,
    "TYPE"   VARCHAR2(50)   NULL,
    "STATUS" VARCHAR2(50)   NULL,
    "LEVEL"  NUMBER         NULL,
    "NAME"   VARCHAR2(100)  NULL,
    "USER"   VARCHAR2(50)   NULL,
    "TABLE"  VARCHAR2(50)   NULL
);
CREATE SEQUENCE reserved_seq START WITH 1 INCREMENT BY 1;

INSERT INTO reserved_words_cols (id,"ORDER","GROUP","SELECT","FROM","WHERE","KEY","VALUE","INDEX","DATE","TYPE","STATUS","LEVEL","NAME","USER","TABLE")
VALUES (reserved_seq.NEXTVAL,1,'admin','SELECT 1','table_a','id = 1','key001','{"x":1}',0,TO_DATE('2024-01-01','YYYY-MM-DD'),'typeA','active',1,'Alice','SYS','USERS');
INSERT INTO reserved_words_cols (id,"ORDER","GROUP","SELECT","FROM","WHERE","KEY","VALUE","INDEX","DATE","TYPE","STATUS","LEVEL","NAME","USER","TABLE")
VALUES (reserved_seq.NEXTVAL,2,'editors','SELECT *','table_b','id > 0','key002','[1,2,3]',1,TO_DATE('2024-06-15','YYYY-MM-DD'),'typeB','inactive',5,'Bob','APP','PRODUCTS');
INSERT INTO reserved_words_cols (id,"ORDER") VALUES (reserved_seq.NEXTVAL,NULL);
COMMIT;

-- ============================================================
-- SECTION 9: Composite Primary Key (3-column PK)
-- ============================================================
CREATE TABLE composite_pk (
    tenant_id    NUMBER       NOT NULL,
    entity_type  VARCHAR2(50) NOT NULL,
    entity_id    NUMBER(19)   NOT NULL,
    label        VARCHAR2(100) NULL,
    payload      CLOB         NULL,
    created_at   TIMESTAMP    DEFAULT SYSDATE,
    CONSTRAINT pk_composite_pk PRIMARY KEY (tenant_id, entity_type, entity_id)
);
CREATE SEQUENCE composite_pk_seq START WITH 1 INCREMENT BY 1;

INSERT ALL
    INTO composite_pk VALUES (1,'user',100,'Tenant1 User 100','{"role":"admin"}',SYSDATE)
    INTO composite_pk VALUES (1,'user',101,'Tenant1 User 101','{"role":"viewer"}',SYSDATE)
    INTO composite_pk VALUES (1,'product',200,'Tenant1 Product 200','{"sku":"SKU-200"}',SYSDATE)
    INTO composite_pk VALUES (2,'user',100,'Tenant2 User 100','{"role":"editor"}',SYSDATE)
    INTO composite_pk VALUES (2,'order',300,'Tenant2 Order 300',NULL,SYSDATE)
    INTO composite_pk VALUES (3,'product',200,'Tenant3 Product 200','{"sku":"SKU-200"}',SYSDATE)
SELECT 1 FROM DUAL;
COMMIT;

-- ============================================================
-- SECTION 10: Multiple UNIQUE Constraints on different columns
-- ============================================================
CREATE TABLE multi_unique (
    id        NUMBER PRIMARY KEY,
    sku       VARCHAR2(50)  NOT NULL,
    barcode   VARCHAR2(50)  NOT NULL,
    serial_no VARCHAR2(100) NULL,
    email     VARCHAR2(100) NULL,
    label     VARCHAR2(200) NULL,
    CONSTRAINT uq_multi_sku     UNIQUE (sku),
    CONSTRAINT uq_multi_barcode UNIQUE (barcode),
    CONSTRAINT uq_multi_serial  UNIQUE (serial_no),
    CONSTRAINT uq_multi_email   UNIQUE (email)
);
CREATE SEQUENCE multi_unique_seq START WITH 1 INCREMENT BY 1;

INSERT ALL
    INTO multi_unique VALUES (multi_unique_seq.NEXTVAL,'SKU-001','BAR-001','SN-001','prod1@example.com','Product 1')
    INTO multi_unique VALUES (multi_unique_seq.NEXTVAL,'SKU-002','BAR-002','SN-002','prod2@example.com','Product 2')
    INTO multi_unique VALUES (multi_unique_seq.NEXTVAL,'SKU-003','BAR-003',NULL,NULL,'Product 3 no serial')
    INTO multi_unique VALUES (multi_unique_seq.NEXTVAL,'SKU-004','BAR-004','SN-004','prod4@example.com','Product 4')
SELECT 1 FROM DUAL;
COMMIT;

-- ============================================================
-- SECTION 11: 3-Level FK Chain (grandparent → parent → child)
-- ============================================================
CREATE TABLE fk_grandparent (
    id   NUMBER PRIMARY KEY,
    name VARCHAR2(100) NOT NULL,
    code VARCHAR2(20)  NOT NULL UNIQUE
);
CREATE SEQUENCE fk_grandparent_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE fk_parent (
    id             NUMBER PRIMARY KEY,
    grandparent_id NUMBER NOT NULL REFERENCES fk_grandparent(id),
    name           VARCHAR2(100) NOT NULL,
    description    VARCHAR2(500) NULL
);
CREATE SEQUENCE fk_parent_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE fk_child (
    id         NUMBER PRIMARY KEY,
    parent_id  NUMBER NOT NULL REFERENCES fk_parent(id),
    name       VARCHAR2(100) NOT NULL,
    value      NUMBER(10,2)  NULL,
    created_at TIMESTAMP     DEFAULT SYSDATE
);
CREATE SEQUENCE fk_child_seq START WITH 1 INCREMENT BY 1;

-- NOTE: INSERT ALL with sequences is broken in Oracle (NEXTVAL evaluated once per statement,
-- not per row, causing duplicate PK values). Individual INSERTs are required.
INSERT INTO fk_grandparent VALUES (fk_grandparent_seq.NEXTVAL,'Region A','REG-A');
INSERT INTO fk_grandparent VALUES (fk_grandparent_seq.NEXTVAL,'Region B','REG-B');
INSERT INTO fk_grandparent VALUES (fk_grandparent_seq.NEXTVAL,'Region C','REG-C');

INSERT INTO fk_parent VALUES (fk_parent_seq.NEXTVAL,1,'City A1','City in Region A');
INSERT INTO fk_parent VALUES (fk_parent_seq.NEXTVAL,1,'City A2','Another city in Region A');
INSERT INTO fk_parent VALUES (fk_parent_seq.NEXTVAL,2,'City B1','City in Region B');
INSERT INTO fk_parent VALUES (fk_parent_seq.NEXTVAL,3,'City C1','City in Region C');

INSERT INTO fk_child VALUES (fk_child_seq.NEXTVAL,1,'District A1-1',100.00,SYSDATE);
INSERT INTO fk_child VALUES (fk_child_seq.NEXTVAL,1,'District A1-2',200.00,SYSDATE);
INSERT INTO fk_child VALUES (fk_child_seq.NEXTVAL,2,'District A2-1',150.00,SYSDATE);
INSERT INTO fk_child VALUES (fk_child_seq.NEXTVAL,3,'District B1-1',300.00,SYSDATE);
INSERT INTO fk_child VALUES (fk_child_seq.NEXTVAL,4,'District C1-1',250.00,SYSDATE);
COMMIT;

-- ============================================================
-- SECTION 12: Self-Referencing FK (category tree)
-- ============================================================
CREATE TABLE self_ref (
    id        NUMBER PRIMARY KEY,
    parent_id NUMBER       NULL REFERENCES self_ref(id),
    name      VARCHAR2(100) NOT NULL,
    depth     NUMBER        DEFAULT 0 NOT NULL,
    path      VARCHAR2(500) NULL
);
CREATE SEQUENCE self_ref_seq START WITH 1 INCREMENT BY 1;

-- Root nodes (depth=0, id 1-3)
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,NULL,'Electronics',0,'/Electronics');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,NULL,'Clothing',0,'/Clothing');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,NULL,'Books',0,'/Books');
-- Level 1 (id 4-8, parent_id references roots above)
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,1,'Computers',1,'/Electronics/Computers');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,1,'Phones',1,'/Electronics/Phones');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,1,'Audio',1,'/Electronics/Audio');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,2,'Men',1,'/Clothing/Men');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,2,'Women',1,'/Clothing/Women');
-- Level 2 (id 9-13, parent_id references level-1 nodes)
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,4,'Laptops',2,'/Electronics/Computers/Laptops');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,4,'Desktops',2,'/Electronics/Computers/Desktops');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,5,'Smartphones',2,'/Electronics/Phones/Smartphones');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,7,'Shirts',2,'/Clothing/Men/Shirts');
INSERT INTO self_ref VALUES (self_ref_seq.NEXTVAL,8,'Dresses',2,'/Clothing/Women/Dresses');
COMMIT;

-- ============================================================
-- SECTION 13: Empty Table (Schema-Only migration test)
-- ============================================================
CREATE TABLE empty_table (
    id          NUMBER PRIMARY KEY,
    name        VARCHAR2(100)  NOT NULL,
    description NCLOB          NULL,
    value       NUMBER(18,6)   NULL,
    active      NUMBER(1)      DEFAULT 1 NOT NULL,
    created_at  TIMESTAMP      DEFAULT SYSDATE,
    CONSTRAINT uq_empty_name UNIQUE (name)
);
CREATE SEQUENCE empty_table_seq START WITH 1 INCREMENT BY 1;
-- Intentionally no rows — used for Schema Only migration tests

-- ============================================================
-- SECTION 14: Wide Table (30 columns)
-- ============================================================
CREATE TABLE wide_table (
    id      NUMBER PRIMARY KEY,
    col_01  VARCHAR2(50)    NULL, col_02  VARCHAR2(50)    NULL,
    col_03  NUMBER(10)      NULL, col_04  NUMBER(10)      NULL,
    col_05  NUMBER(10,2)    NULL, col_06  NUMBER(10,2)    NULL,
    col_07  TIMESTAMP       NULL, col_08  TIMESTAMP       NULL,
    col_09  NUMBER(1)       NULL, col_10  NUMBER(1)       NULL,
    col_11  NVARCHAR2(200)  NULL, col_12  NVARCHAR2(200)  NULL,
    col_13  NUMBER(19)      NULL, col_14  NUMBER(19)      NULL,
    col_15  BINARY_DOUBLE   NULL, col_16  BINARY_DOUBLE   NULL,
    col_17  DATE            NULL, col_18  DATE            NULL,
    col_19  TIMESTAMP(3)    NULL, col_20  TIMESTAMP(3)    NULL,
    col_21  BLOB            NULL, col_22  RAW(16)         NULL,
    col_23  CLOB            NULL, col_24  NUMBER(5)       NULL,
    col_25  NUMBER(3)       NULL, col_26  CHAR(5)         NULL,
    col_27  NUMBER(19,4)    NULL, col_28  BINARY_FLOAT    NULL,
    col_29  NVARCHAR2(100)  NULL, col_30  XMLTYPE         NULL
);
CREATE SEQUENCE wide_table_seq START WITH 1 INCREMENT BY 1;

INSERT INTO wide_table (id,col_01,col_02,col_03,col_04,col_05,col_06,col_07,col_08,col_09,col_10,
    col_11,col_12,col_13,col_14,col_15,col_16,col_17,col_18,col_19,col_20,
    col_21,col_22,col_23,col_24,col_25,col_26,col_27,col_28,col_29,col_30)
VALUES (wide_table_seq.NEXTVAL,'alpha','beta',1,2,1.11,2.22,
    TIMESTAMP '2024-01-01 00:00:00',TIMESTAMP '2024-06-15 00:00:00',1,0,
    N'unicode 日本語',N'emoji test',9999999,-9999999,1.1e0,2.2e0,
    TO_DATE('2024-01-01','YYYY-MM-DD'),TO_DATE('2024-06-15','YYYY-MM-DD'),
    TIMESTAMP '2024-01-01 10:30:00.123',TIMESTAMP '2024-12-31 23:59:59.999',
    UTL_RAW.CAST_TO_RAW('DEADBEEF'),HEXTORAW('6BA7B8109DAD11D180B400C04FD430C8'),
    RPAD('X',1000,'X'),32000,200,'ABCDE',9999.9999,3.14e0f,'wide test column',
    XMLTYPE('<r><v>1</v></r>'));
INSERT INTO wide_table (id,col_01) VALUES (wide_table_seq.NEXTVAL,NULL);
COMMIT;

-- ============================================================
-- SECTION 15: Batch Test (1200 rows — verifies 1000-row batch processing)
-- ============================================================
CREATE TABLE batch_test (
    id         NUMBER PRIMARY KEY,
    seq_num    NUMBER(6)      NOT NULL,
    label      VARCHAR2(50)   NOT NULL,
    value_int  NUMBER(10)     NULL,
    value_dec  NUMBER(12,4)   NULL,
    value_str  VARCHAR2(100)  NULL,
    created_at TIMESTAMP      DEFAULT SYSDATE
);
CREATE SEQUENCE batch_test_seq START WITH 1 INCREMENT BY 1;

INSERT INTO batch_test (id, seq_num, label, value_int, value_dec, value_str)
SELECT
    batch_test_seq.NEXTVAL,
    LEVEL,
    'Row_' || LPAD(LEVEL, 4, '0'),
    MOD(LEVEL, 1000),
    LEVEL * 3.14159,
    CASE MOD(LEVEL, 5)
        WHEN 0 THEN 'fizzbuzz' WHEN 1 THEN 'one'
        WHEN 2 THEN 'two'      WHEN 3 THEN 'three'
        ELSE 'four'
    END
FROM DUAL
CONNECT BY LEVEL <= 1200;
COMMIT;
