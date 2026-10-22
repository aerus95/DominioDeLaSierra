using System.Data;
using System.Globalization;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DominioDeLaSierra.Infrastructure.Checkout;

public sealed class RedsysOperationNumberAllocator(ApplicationDbContext dbContext) : IRedsysOperationNumbers
{
    public const string SequenceName = "redsys_operation_numbers";

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT nextval('{SequenceName}')";
        var raw = await command.ExecuteScalarAsync(cancellationToken);
        var value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        return RedsysOperationNumber.FromSequence(value);
    }
}
