-- The SQL Server database the shop-sql cases of app-check read (tools/app-check/prompts.json).
--
--   sqlcmd -S . -E -b -i tools\app-check\sql\ValidationPhase3Test.sql
--
-- Safe to run again: the database, each table and the rows are created only when missing, so a
-- second run changes nothing and never resets rows an app has since edited.
--
-- The shape is the one docs/superpowers/plans/2026-09-11-validation-phase-3-run-notes.md
-- describes, because the harness and those notes expect it:
--   Customer, a master: Id, Code, Status, Name. 5 rows, Code unique C001-C005, Status one of
--     Active / Inactive / Pending.
--   [Order], a transaction: Id, CustomerId (a foreign key to Customer), OrderDate, Total. 4 rows:
--     customer 1 has 2 orders, customers 2 and 3 have 1 each, customers 4 and 5 have none.

IF DB_ID(N'ValidationPhase3Test') IS NULL
    CREATE DATABASE [ValidationPhase3Test];
GO

USE [ValidationPhase3Test];
GO

IF OBJECT_ID(N'dbo.Customer', N'U') IS NULL
    CREATE TABLE dbo.Customer
    (
        Id     INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
        Code   NVARCHAR(10)       NOT NULL CONSTRAINT UQ_Customer_Code UNIQUE,
        Status NVARCHAR(20)       NOT NULL,
        Name   NVARCHAR(100)      NOT NULL
    );
GO

IF OBJECT_ID(N'dbo.[Order]', N'U') IS NULL
    CREATE TABLE dbo.[Order]
    (
        Id         INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Order PRIMARY KEY,
        CustomerId INT                NOT NULL
            CONSTRAINT FK_Order_Customer FOREIGN KEY REFERENCES dbo.Customer (Id),
        OrderDate  DATE               NOT NULL,
        Total      DECIMAL(18, 2)     NOT NULL
    );
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Customer)
BEGIN
    SET IDENTITY_INSERT dbo.Customer ON;

    INSERT INTO dbo.Customer (Id, Code, Status, Name)
    VALUES
        (1, N'C001', N'Active',   N'Maple and Co'),
        (2, N'C002', N'Active',   N'Harbor Cafe'),
        (3, N'C003', N'Inactive', N'Sunset Grocers'),
        (4, N'C004', N'Pending',  N'Riverside Deli'),
        (5, N'C005', N'Active',   N'Northwind Traders');

    SET IDENTITY_INSERT dbo.Customer OFF;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.[Order])
BEGIN
    SET IDENTITY_INSERT dbo.[Order] ON;

    INSERT INTO dbo.[Order] (Id, CustomerId, OrderDate, Total)
    VALUES
        (1, 1, '2026-08-05', 120.50),
        (2, 1, '2026-08-19', 75.00),
        (3, 2, '2026-08-14', 210.25),
        (4, 3, '2026-08-08', 42.10);

    SET IDENTITY_INSERT dbo.[Order] OFF;
END;
GO
