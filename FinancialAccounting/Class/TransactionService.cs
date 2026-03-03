using FinancialAccounting;
using FinancialAccounting.Class.Models;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NpgsqlTypes;


public class TransactionService
{
    private readonly DatabaseManager _db;
    private readonly string _username;
    private readonly int _accountId;

    public TransactionService(DatabaseManager db, string username, int accountId)
    {
        _db = db;
        _username = username;
        _accountId = accountId;
    }

    public string SaveTransactions(IEnumerable<TransactionRecord> transactions, string username, int accountId)
    {
        if (transactions == null || !transactions.Any())
            return "Нет данных для сохранения.";

        var connection = _db.GetOpenConnection();

        foreach (var transaction in transactions)
        {
            // 1. Категория
            int categoryId;
            using (var checkCmd = new NpgsqlCommand(
                "SELECT id FROM categories WHERE name = @name LIMIT 1",
                connection))
            {
                checkCmd.Parameters.AddWithValue("name", transaction.Category);
                var result = checkCmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                {
                    categoryId = Convert.ToInt32(result);
                }
                else
                {
                    using (var insertCategoryCmd = new NpgsqlCommand(
                        "INSERT INTO categories (name) VALUES (@name) RETURNING id",
                        connection))
                    {
                        insertCategoryCmd.Parameters.AddWithValue("name", transaction.Category);
                        categoryId = Convert.ToInt32(insertCategoryCmd.ExecuteScalar());
                    }
                }
            }

            // 2. Дата
            if (!DateTime.TryParseExact(
                    transaction.Date,
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime dt))
            {
                dt = DateTime.Now;
            }

            // 3. Сумма и тип
            string rawAmount = transaction.Amount.Trim();
            decimal.TryParse(rawAmount, NumberStyles.Number, new CultureInfo("ru-RU"), out decimal amountValue);
            string typeValue = rawAmount.StartsWith("+") ? "Income" : "Expense";

            // 4. Проверка дублей (без userid)
            using (var duplicateCheckCmd = new NpgsqlCommand(@"
                SELECT COUNT(*) 
                FROM transactions 
                WHERE date = @date 
                  AND amount = @amount 
                  AND description = @description 
                  AND categoryid = @categoryid
                  AND accountid = @accountid", connection))
            {
                duplicateCheckCmd.Parameters.AddWithValue("date", dt);
                duplicateCheckCmd.Parameters.AddWithValue("amount", amountValue);
                duplicateCheckCmd.Parameters.AddWithValue("description", transaction.Description ?? "");
                duplicateCheckCmd.Parameters.AddWithValue("categoryid", categoryId);
                duplicateCheckCmd.Parameters.AddWithValue("accountid", _accountId);

                int count = Convert.ToInt32(duplicateCheckCmd.ExecuteScalar());
                if (count > 0) continue;
            }

            // 5. Вставка транзакции (ИСПРАВЛЕНО)
            using (var insertTransactionCmd = new NpgsqlCommand(@"
                INSERT INTO transactions 
                    (date, amount, type, categoryid, description, accountid)
                VALUES 
                    (@date, @amount, @type::transaction_type, @categoryid, @description, @accountid)", connection))
            {
                insertTransactionCmd.Parameters.AddWithValue("date", dt);
                insertTransactionCmd.Parameters.AddWithValue("amount", amountValue);

                // Просто строка
                insertTransactionCmd.Parameters.AddWithValue("type", typeValue);

                insertTransactionCmd.Parameters.AddWithValue("categoryid", categoryId);
                insertTransactionCmd.Parameters.AddWithValue("description", transaction.Description ?? "");
                insertTransactionCmd.Parameters.AddWithValue("accountid", _accountId);

                insertTransactionCmd.ExecuteNonQuery();
            }
        }



        return "OK";
    }
}
