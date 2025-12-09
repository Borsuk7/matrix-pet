SET NOCOUNT ON;

DECLARE @Central int = (SELECT BranchId FROM dbo.LibraryBranch WHERE Name = N'Central Library');
DECLARE @Riverside int = (SELECT BranchId FROM dbo.LibraryBranch WHERE Name = N'Riverside Branch');
DECLARE @Petro int = (SELECT MemberId FROM dbo.Member WHERE Email = N'petro@example.local');
DECLARE @Dune int = (SELECT BookId FROM dbo.Book WHERE Title = N'Dune');
DECLARE @Nineteen84 int = (SELECT BookId FROM dbo.Book WHERE Title = N'1984');

PRINT 'Initial inventory snapshot (Central)';
SELECT * FROM dbo.fn_InventorySummary(@Central) ORDER BY Title;

PRINT 'Borrow Dune (Central) for Petro';
EXEC dbo.usp_BorrowBook
  @BranchId = @Central,
  @BookId = @Dune,
  @MemberId = @Petro,
  @Days = 7;

PRINT 'Inventory after borrow (Central)';
SELECT * FROM dbo.fn_InventorySummary(@Central) ORDER BY Title;

PRINT 'Open loans';
SELECT l.LoanId, l.MemberId, l.BranchId, l.BookId, l.LoanedAtUtc, l.DueAtUtc, l.ReturnedAtUtc
FROM dbo.Loan l
WHERE l.ReturnedAtUtc IS NULL
ORDER BY l.LoanId DESC;

PRINT 'Demonstrate rollback with an intentional error';
BEGIN TRY
  BEGIN TRAN;

  UPDATE dbo.Inventory
    SET AvailableCopies = AvailableCopies - 1
  WHERE BranchId = @Riverside AND BookId = @Nineteen84;

  INSERT dbo.InventoryAudit (BranchId, BookId, ChangeDelta, Reason)
  VALUES (@Riverside, @Nineteen84, -1, N'Manual borrow simulation');

  THROW 50999, 'Intentional failure to show rollback.', 1;

  COMMIT;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  PRINT CONCAT('Caught error: ', ERROR_MESSAGE());
END CATCH;

PRINT 'Verify rollback worked (Riverside inventory should be unchanged by failed manual transaction)';
SELECT * FROM dbo.fn_InventorySummary(@Riverside) ORDER BY Title;

PRINT 'Return the most recent open loan';
DECLARE @loanId int =
(
  SELECT TOP (1) LoanId
  FROM dbo.Loan
  WHERE ReturnedAtUtc IS NULL
  ORDER BY LoanId DESC
);

EXEC dbo.usp_ReturnBook @LoanId = @loanId;

PRINT 'Audit trail';
SELECT TOP (50) *
FROM dbo.InventoryAudit
ORDER BY AuditId DESC;

PRINT 'Overdue fee example: mark a loan returned late';
DECLARE @demoLoanId int =
(
  SELECT TOP (1) LoanId FROM dbo.Loan ORDER BY LoanId DESC
);

UPDATE dbo.Loan
  SET DueAtUtc = DATEADD(day, -5, sysutcdatetime()),
      ReturnedAtUtc = sysutcdatetime()
WHERE LoanId = @demoLoanId;

SELECT
  LoanId,
  DueAtUtc,
  ReturnedAtUtc,
  dbo.fn_OverdueFee(DueAtUtc, ReturnedAtUtc, 1.50) AS OverdueFee
FROM dbo.Loan
WHERE LoanId = @demoLoanId;
