IF DB_ID('Matrix') IS NOT NULL
BEGIN
  ALTER DATABASE Matrix SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  DROP DATABASE Matrix;
END
GO

CREATE DATABASE Matrix;
GO



SET NOCOUNT ON;
GO

CREATE TABLE dbo.Author
(
  AuthorId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Author PRIMARY KEY,
  FirstName      nvarchar(80) NOT NULL,
  LastName       nvarchar(80) NOT NULL,
  BirthDate      date NULL,
  Country        nvarchar(80) NULL,
  CreatedAtUtc   datetime2(0) NOT NULL CONSTRAINT DF_Author_CreatedAtUtc DEFAULT (sysutcdatetime())
);
GO

CREATE TABLE dbo.Book
(
  BookId         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Book PRIMARY KEY,
  AuthorId       int NOT NULL,
  Title          nvarchar(200) NOT NULL,
  Isbn           varchar(20) NULL,
  PublishedYear  int NULL,
  Price          decimal(10,2) NOT NULL CONSTRAINT DF_Book_Price DEFAULT (0),
  CreatedAtUtc   datetime2(0) NOT NULL CONSTRAINT DF_Book_CreatedAtUtc DEFAULT (sysutcdatetime()),
  CONSTRAINT FK_Book_Author FOREIGN KEY (AuthorId) REFERENCES dbo.Author(AuthorId)
);
GO

CREATE UNIQUE INDEX UX_Book_Isbn ON dbo.Book(Isbn) WHERE Isbn IS NOT NULL;
GO

CREATE TABLE dbo.LibraryBranch
(
  BranchId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LibraryBranch PRIMARY KEY,
  Name           nvarchar(120) NOT NULL,
  City           nvarchar(120) NOT NULL,
  CreatedAtUtc   datetime2(0) NOT NULL CONSTRAINT DF_Branch_CreatedAtUtc DEFAULT (sysutcdatetime())
);
GO

CREATE TABLE dbo.Inventory
(
  BranchId       int NOT NULL,
  BookId         int NOT NULL,
  TotalCopies    int NOT NULL,
  AvailableCopies int NOT NULL,
  UpdatedAtUtc   datetime2(0) NOT NULL CONSTRAINT DF_Inventory_UpdatedAtUtc DEFAULT (sysutcdatetime()),
  CONSTRAINT PK_Inventory PRIMARY KEY (BranchId, BookId),
  CONSTRAINT FK_Inventory_Branch FOREIGN KEY (BranchId) REFERENCES dbo.LibraryBranch(BranchId),
  CONSTRAINT FK_Inventory_Book FOREIGN KEY (BookId) REFERENCES dbo.Book(BookId),
  CONSTRAINT CK_Inventory_NonNegative CHECK (TotalCopies >= 0 AND AvailableCopies >= 0),
  CONSTRAINT CK_Inventory_AvailableLETotal CHECK (AvailableCopies <= TotalCopies)
);
GO

CREATE TABLE dbo.Member
(
  MemberId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Member PRIMARY KEY,
  FullName       nvarchar(200) NOT NULL,
  Email          nvarchar(200) NOT NULL,
  CreatedAtUtc   datetime2(0) NOT NULL CONSTRAINT DF_Member_CreatedAtUtc DEFAULT (sysutcdatetime())
);
GO

CREATE UNIQUE INDEX UX_Member_Email ON dbo.Member(Email);
GO

CREATE TABLE dbo.Loan
(
  LoanId         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Loan PRIMARY KEY,
  BranchId       int NOT NULL,
  BookId         int NOT NULL,
  MemberId       int NOT NULL,
  LoanedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_Loan_LoanedAtUtc DEFAULT (sysutcdatetime()),
  DueAtUtc       datetime2(0) NOT NULL,
  ReturnedAtUtc  datetime2(0) NULL,
  CONSTRAINT FK_Loan_Branch FOREIGN KEY (BranchId) REFERENCES dbo.LibraryBranch(BranchId),
  CONSTRAINT FK_Loan_Book FOREIGN KEY (BookId) REFERENCES dbo.Book(BookId),
  CONSTRAINT FK_Loan_Member FOREIGN KEY (MemberId) REFERENCES dbo.Member(MemberId)
);
GO

CREATE INDEX IX_Loan_OpenLoans ON dbo.Loan(BranchId, BookId) INCLUDE (ReturnedAtUtc) WHERE ReturnedAtUtc IS NULL;
GO

CREATE TABLE dbo.InventoryAudit
(
  AuditId        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryAudit PRIMARY KEY,
  BranchId       int NOT NULL,
  BookId         int NOT NULL,
  ChangeDelta    int NOT NULL,
  Reason         nvarchar(200) NOT NULL,
  ChangedAtUtc   datetime2(0) NOT NULL CONSTRAINT DF_InvAudit_ChangedAtUtc DEFAULT (sysutcdatetime())
);
GO

CREATE OR ALTER PROCEDURE dbo.usp_BorrowBook
  @BranchId int,
  @BookId int,
  @MemberId int,
  @Days int = 14
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (SELECT 1 FROM dbo.Member WHERE MemberId = @MemberId)
      THROW 50010, 'Member does not exist.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Book WHERE BookId = @BookId)
      THROW 50011, 'Book does not exist.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.LibraryBranch WHERE BranchId = @BranchId)
      THROW 50012, 'Branch does not exist.', 1;

    DECLARE @available int;

    SELECT @available = i.AvailableCopies
    FROM dbo.Inventory i WITH (UPDLOCK, ROWLOCK)
    WHERE i.BranchId = @BranchId AND i.BookId = @BookId;

    IF @available IS NULL
      THROW 50013, 'No inventory record for this book at this branch.', 1;

    IF @available <= 0
      THROW 50014, 'No available copies.', 1;

    UPDATE dbo.Inventory
      SET AvailableCopies = AvailableCopies - 1,
          UpdatedAtUtc = sysutcdatetime()
    WHERE BranchId = @BranchId AND BookId = @BookId;

    INSERT dbo.InventoryAudit (BranchId, BookId, ChangeDelta, Reason)
    VALUES (@BranchId, @BookId, -1, CONCAT('Borrow by MemberId=', @MemberId));

    INSERT dbo.Loan (BranchId, BookId, MemberId, DueAtUtc)
    VALUES (@BranchId, @BookId, @MemberId, DATEADD(day, @Days, sysutcdatetime()));

    COMMIT;
  END TRY
  BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;

    DECLARE @msg nvarchar(2048) = ERROR_MESSAGE();
    DECLARE @num int = ERROR_NUMBER();
    THROW @num, @msg, 1;
  END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ReturnBook
  @LoanId int
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  BEGIN TRY
    BEGIN TRAN;

    DECLARE @BranchId int, @BookId int, @MemberId int;

    SELECT @BranchId = BranchId, @BookId = BookId, @MemberId = MemberId
    FROM dbo.Loan WITH (UPDLOCK, ROWLOCK)
    WHERE LoanId = @LoanId;

    IF @BranchId IS NULL
      THROW 50020, 'Loan not found.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Loan WHERE LoanId = @LoanId AND ReturnedAtUtc IS NOT NULL)
      THROW 50021, 'Loan already returned.', 1;

    UPDATE dbo.Loan
      SET ReturnedAtUtc = sysutcdatetime()
    WHERE LoanId = @LoanId;

    UPDATE dbo.Inventory
      SET AvailableCopies = AvailableCopies + 1,
          UpdatedAtUtc = sysutcdatetime()
    WHERE BranchId = @BranchId AND BookId = @BookId;

    INSERT dbo.InventoryAudit (BranchId, BookId, ChangeDelta, Reason)
    VALUES (@BranchId, @BookId, +1, CONCAT('Return by MemberId=', @MemberId, ' LoanId=', @LoanId));

    COMMIT;
  END TRY
  BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;

    DECLARE @msg nvarchar(2048) = ERROR_MESSAGE();
    DECLARE @num int = ERROR_NUMBER();
    THROW @num, @msg, 1;
  END CATCH
END
GO



SET NOCOUNT ON;
GO

INSERT dbo.Author (FirstName, LastName, BirthDate, Country)
VALUES
  (N'George', N'Orwell', '1903-06-25', N'United Kingdom'),
  (N'Agatha', N'Christie', '1890-09-15', N'United Kingdom'),
  (N'Frank', N'Herbert', '1920-10-08', N'United States'),
  (N'Haruki', N'Murakami', '1949-01-12', N'Japan');
GO

INSERT dbo.Book (AuthorId, Title, Isbn, PublishedYear, Price)
SELECT a.AuthorId, b.Title, b.Isbn, b.PublishedYear, b.Price
FROM dbo.Author a
JOIN (VALUES
  (N'Orwell',   N'1984',                      '9780451524935', 1949, 12.99),
  (N'Orwell',   N'Animal Farm',               '9780451526342', 1945,  9.99),
  (N'Christie', N'Murder on the Orient Express','9780062693662',1934, 11.50),
  (N'Herbert',  N'Dune',                      '9780441172719', 1965, 14.95),
  (N'Murakami', N'Kafka on the Shore',        '9781400079278', 2002, 13.49)
) b(LastName, Title, Isbn, PublishedYear, Price)
  ON a.LastName = b.LastName;
GO

INSERT dbo.LibraryBranch (Name, City)
VALUES
  (N'Central Library', N'Kyiv'),
  (N'Riverside Branch', N'Kyiv'),
  (N'Old Town Branch', N'Lviv');
GO

INSERT dbo.Member (FullName, Email)
VALUES
  (N'Petro Demo', N'petro@example.local'),
  (N'Alex Test', N'alex@example.local'),
  (N'Iryna Sample', N'iryna@example.local');
GO

DECLARE @Central int = (SELECT BranchId FROM dbo.LibraryBranch WHERE Name = N'Central Library');
DECLARE @Riverside int = (SELECT BranchId FROM dbo.LibraryBranch WHERE Name = N'Riverside Branch');
DECLARE @OldTown int = (SELECT BranchId FROM dbo.LibraryBranch WHERE Name = N'Old Town Branch');

INSERT dbo.Inventory (BranchId, BookId, TotalCopies, AvailableCopies)
SELECT @Central, b.BookId,
       CASE WHEN b.Title IN (N'1984', N'Dune') THEN 3 ELSE 2 END,
       CASE WHEN b.Title IN (N'1984', N'Dune') THEN 3 ELSE 2 END
FROM dbo.Book b;

INSERT dbo.Inventory (BranchId, BookId, TotalCopies, AvailableCopies)
SELECT @Riverside, b.BookId, 1, 1
FROM dbo.Book b
WHERE b.Title IN (N'1984', N'Animal Farm', N'Dune');

INSERT dbo.Inventory (BranchId, BookId, TotalCopies, AvailableCopies)
SELECT @OldTown, b.BookId, 2, 2
FROM dbo.Book b
WHERE b.Title IN (N'Murder on the Orient Express', N'Kafka on the Shore');
GO

