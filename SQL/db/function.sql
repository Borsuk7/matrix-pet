SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_AuthorDisplayName (@AuthorId int)
RETURNS nvarchar(200)
AS
BEGIN
  DECLARE @name nvarchar(200);

  SELECT @name = CONCAT(LastName, N', ', FirstName)
  FROM dbo.Author
  WHERE AuthorId = @AuthorId;

  RETURN @name;
END
GO

CREATE OR ALTER FUNCTION dbo.fn_BookAgeYears (@PublishedYear int)
RETURNS int
AS
BEGIN
  IF @PublishedYear IS NULL RETURN NULL;
  RETURN YEAR(GETUTCDATE()) - @PublishedYear;
END
GO

SELECT TOP (10)
  a.AuthorId,
  dbo.fn_AuthorDisplayName(a.AuthorId) AS DisplayName
FROM dbo.Author a
ORDER BY a.AuthorId;
GO


CREATE OR ALTER FUNCTION dbo.fn_BooksByAuthor (@AuthorId int)
RETURNS TABLE
AS
RETURN
(
  SELECT
    b.BookId,
    b.Title,
    b.Isbn,
    b.PublishedYear,
    dbo.fn_BookAgeYears(b.PublishedYear) AS AgeYears,
    b.Price
  FROM dbo.Book b
  WHERE b.AuthorId = @AuthorId
);
GO

CREATE OR ALTER FUNCTION dbo.fn_InventorySummary (@BranchId int)
RETURNS @t TABLE
(
  BranchId int NOT NULL,
  BookId int NOT NULL,
  Title nvarchar(200) NOT NULL,
  TotalCopies int NOT NULL,
  AvailableCopies int NOT NULL,
  OpenLoans int NOT NULL
)
AS
BEGIN
  INSERT @t (BranchId, BookId, Title, TotalCopies, AvailableCopies, OpenLoans)
  SELECT
    i.BranchId,
    i.BookId,
    b.Title,
    i.TotalCopies,
    i.AvailableCopies,
    (SELECT COUNT(*) FROM dbo.Loan l WHERE l.BranchId = i.BranchId AND l.BookId = i.BookId AND l.ReturnedAtUtc IS NULL) AS OpenLoans
  FROM dbo.Inventory i
  JOIN dbo.Book b ON b.BookId = i.BookId
  WHERE i.BranchId = @BranchId;

  RETURN;
END
GO

CREATE OR ALTER FUNCTION dbo.fn_OverdueFee
(
  @DueAtUtc datetime2(0),
  @ReturnedAtUtc datetime2(0),
  @DailyFee decimal(10,2)
)
RETURNS decimal(10,2)
AS
BEGIN
  IF @ReturnedAtUtc IS NULL RETURN 0;

  DECLARE @daysLate int = DATEDIFF(day, @DueAtUtc, @ReturnedAtUtc);

  IF @daysLate <= 0 RETURN 0;

  RETURN CAST(@daysLate AS decimal(10,2)) * @DailyFee;
END
GO