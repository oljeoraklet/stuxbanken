using System.Transactions;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;
using System.Data;
using System.Text;

namespace MyApp.Namespace
{
    public class APIController : Controller
    {
        // GET: APIController

        List<Transaction> transactions = [];
        List<Category> categories = [];

        public ActionResult GetTransactions()
        {
            if (!GetFromDatabaseTransactions())
            {
                UpdateTransactions();
            }

            return View("GetTransactions", new TransactionsViewModel
            {
                Transactions = transactions,
                Categories = categories
            });
        }
        public bool GetFromDatabaseTransactions()
        {
            // If Database is empty, fetch from API through UpdateTransactions
            // Else fetch the transactions from the database and return them

            string commandText = "SELECT COUNT(*) FROM Transactions";
            int count;

            //Felhantering om det inte går att koppla till databasen
            using (var connection = new SqliteConnection("Data Source=database.db"))
            {
                connection.Open();

                using (var command = new SqliteCommand(commandText, connection))
                {
                    count = Convert.ToInt32(command.ExecuteScalar());
                }

                connection.Close();
            }

            if (count == 0)
            {
                Console.WriteLine("The table is empty");
                // The table is empty
                // Fetch from API through UpdateTransactions
                return false;
            }
            else
            {
                //Get all transactions from the database and connect them with the correct categoryName

                using (var connection = new SqliteConnection("Data Source=database.db"))
                {
                    connection.Open();

                    string sqlQuery = @"
                    SELECT Transactions.TransactionID, Transactions.BookingDate, Transactions.TransactionDate, Transactions.Reference, Transactions.Amount, Transactions.Balance, Categories.CategoryName
                    FROM Transactions
                    JOIN Categories ON CategoryTransaction.CategoryID = Categories.CategoryID
                    JOIN CategoryTransaction ON Transactions.TransactionID = CategoryTransaction.TransactionID
                    ORDER BY Transactions.TransactionID DESC;";

                    using (var command = new SqliteCommand(sqlQuery, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Transaction transaction = new()
                                {
                                    TransactionID = reader.GetInt32(0),
                                    BookingDate = reader.GetString(1),
                                    TransactionDate = reader.GetString(2),
                                    Reference = reader.GetString(3),
                                    Amount = reader.GetDouble(4),
                                    Balance = reader.GetDouble(5),
                                    CategoryName = reader.GetString(6)
                                };
                                transactions.Add(transaction);
                            }
                        }
                    }
                    string sqlQuery2 = @"SELECT CategoryID, CategoryName FROM Categories";
                    using (var command = new SqliteCommand(sqlQuery2, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var category = new Category
                                {
                                    CategoryID = reader.GetInt32(0),
                                    CategoryName = reader.GetString(1)
                                };
                                categories.Add(category);
                            }
                        }
                    }
                }

                return true;

            }
        }
        public ActionResult UpdateTransactions()
        {
            string jsonResult;
            List<Transaction> notInsertedTransactions;
            HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer 1b9f0889f6c6e4e40f04f0c3b21bb76ae39762a8");
            using (HttpResponseMessage response =
                client.GetAsync("https://bank.stuxberg.se/api/iban/SE4550000000058398257466/").Result)
            // Felhantering för om det inte går att hämta från API
            {
                using (HttpContent content = response.Content)
                {
                    jsonResult = content.ReadAsStringAsync().Result;
                    notInsertedTransactions = JsonSerializer.Deserialize<List<Transaction>>(jsonResult);
                    //Insert all transaction into db using parameterized query
                    //Felhantering om det inte går att koppla till databasen
                    using (var connection = new SqliteConnection("Datasource=database.db"))
                    {
                        connection.Open();

                        string sqlQuery = "INSERT OR REPLACE INTO Transactions (TransactionID, BookingDate, TransactionDate, Reference, Amount, Balance) VALUES (@TransactionID, @BookingDate, @TransactionDate, @Reference, @Amount, @Balance)";
                        foreach (var transaction in notInsertedTransactions)
                        {
                            //Check if reference is in the category reference table
                            string commandText = "SELECT COUNT(*) FROM CategoryTransaction WHERE TransactionID = @TransactionID";
                            int count;
                            // string categoryName = string.Empty;
                            using (var command = new SqliteCommand(commandText, connection))
                            {
                                command.Parameters.AddWithValue("@TransactionID", transaction.TransactionID);
                                count = Convert.ToInt32(command.ExecuteScalar());
                            }
                            if (count == 0)
                            {
                                //If not, add it to the category reference table and connect it with the category "Uncategorized"
                                string sqlQuery2 = "INSERT INTO CategoryTransaction (TransactionID, CategoryID) VALUES (@TransactionID, @CategoryID)";
                                using (var command = new SqliteCommand(sqlQuery2, connection))
                                {
                                    command.Parameters.AddWithValue("@TransactionID", transaction.TransactionID);
                                    command.Parameters.AddWithValue("@CategoryID", 1);
                                    command.ExecuteNonQuery();
                                }


                            }
                            using (var command = new SqliteCommand(sqlQuery, connection))
                            {
                                command.Parameters.AddWithValue("@TransactionID", transaction.TransactionID);
                                command.Parameters.AddWithValue("@BookingDate", transaction.BookingDate);
                                command.Parameters.AddWithValue("@TransactionDate", transaction.TransactionDate);
                                command.Parameters.AddWithValue("@Reference", transaction.Reference);
                                command.Parameters.AddWithValue("@Amount", transaction.Amount);
                                command.Parameters.AddWithValue("@Balance", transaction.Balance);
                                command.ExecuteNonQuery();
                            }
                        }
                        connection.Close();
                    }
                }

            }
            return GetTransactions();
        }

        public ActionResult AddCategory()
        {

            return View("AddCategory");
        }
        public ActionResult CreateCategory(Category category)
        {
            Console.WriteLine(category.CategoryName);
            using (var connection = new SqliteConnection("Data Source=database.db"))
            {
                connection.Open();
                string sqlQuery = "INSERT INTO Categories (CategoryName) VALUES (@CategoryName)";
                using (var command = new SqliteCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@CategoryName", category.CategoryName);
                    command.ExecuteNonQuery();
                }
                connection.Close();
            }
            return RedirectToAction("GetTransactions");
        }
        public ActionResult AssignCategories()
        {
            if (!GetFromDatabaseTransactions())
            {
                UpdateTransactions();
            }
            return View("AssignCategories", new TransactionsViewModel
            {
                Transactions = transactions,
                Categories = categories
            });
        }
        [HttpPost]
        public ActionResult AssignCategories(UpdateTransactionsViewModel updates)
        {
            if (!ModelState.IsValid)
            {
                Console.WriteLine("Model is not valid");
            }
            foreach (var update in updates.UpdatedCategories)
            {
                if (update.CategoryID == 0)
                {
                    continue;
                }
                using (var connection = new SqliteConnection("Data Source=database.db"))
                {
                    connection.Open();
                    string sqlQuery = "INSERT OR REPLACE INTO CategoryTransaction (TransactionID, CategoryID) VALUES (@TransactionID, @CategoryID)";
                    using (var command = new SqliteCommand(sqlQuery, connection))
                    {
                        command.Parameters.AddWithValue("@TransactionID", update.TransactionID);
                        command.Parameters.AddWithValue("@CategoryID", update.CategoryID);
                        command.ExecuteNonQuery();
                    }
                    connection.Close();
                }
            }

            return RedirectToAction("AssignCategories");
        }


        public ActionResult Report()
        {
            if (!GetFromDatabaseTransactions())
            {
                UpdateTransactions();
            }
            return View("Report", categories);
        }
        public ReportViewModel getTransactionsForReport(int categoryID)
        {
            if (categoryID == 0)
            {
                using (var connection = new SqliteConnection("Data Source=database.db"))
                {
                    //return all transactions with the selected categoryID and sum up all the expensenes and incomes in new columns
                    connection.Open();
                    string sqlQuery = @"
                    SELECT Transactions.TransactionID, Transactions.BookingDate, Transactions.TransactionDate, Transactions.Reference, Transactions.Amount, Transactions.Balance, Categories.CategoryName
                    FROM Transactions
                    JOIN Categories ON CategoryTransaction.CategoryID = Categories.CategoryID
                    JOIN CategoryTransaction ON Transactions.TransactionID = CategoryTransaction.TransactionID
                    ORDER BY Transactions.TransactionID DESC;";
                    using (var command = new SqliteCommand(sqlQuery, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Transaction transaction = new()
                                {
                                    TransactionID = reader.GetInt32(0),
                                    BookingDate = reader.GetString(1),
                                    TransactionDate = reader.GetString(2),
                                    Reference = reader.GetString(3),
                                    Amount = reader.GetDouble(4),
                                    Balance = reader.GetDouble(5),
                                    CategoryName = reader.GetString(6)
                                };
                                transactions.Add(transaction);
                            }
                        }
                    }
                    connection.Close();
                    return new ReportViewModel()
                    {
                        Transactions = transactions,
                        TotalExpenses = transactions.Where(t => t.Amount < 0).Sum(t => t.Amount),
                        TotalIncomes = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount),
                        categoryID = categoryID
                    };
                }
            }
            else
            {

                using (var connection = new SqliteConnection("Data Source=database.db"))
                {
                    //return all transactions with the selected categoryID and sum up all the expensenes and incomes in new columns
                    connection.Open();
                    string sqlQuery = @"
                                        SELECT Transactions.TransactionID, Transactions.BookingDate, Transactions.TransactionDate, Transactions.Reference, Transactions.Amount, Transactions.Balance, Categories.CategoryName
                                        FROM Transactions
                                        JOIN Categories ON CategoryTransaction.CategoryID = Categories.CategoryID
                                        JOIN CategoryTransaction ON Transactions.TransactionID = CategoryTransaction.TransactionID
                                        WHERE Categories.CategoryID = @CategoryID
                                        ORDER BY Transactions.TransactionID DESC;";
                    using (var command = new SqliteCommand(sqlQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CategoryID", categoryID);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Transaction transaction = new()
                                {
                                    TransactionID = reader.GetInt32(0),
                                    BookingDate = reader.GetString(1),
                                    TransactionDate = reader.GetString(2),
                                    Reference = reader.GetString(3),
                                    Amount = reader.GetDouble(4),
                                    Balance = reader.GetDouble(5),
                                    CategoryName = reader.GetString(6)
                                };
                                transactions.Add(transaction);
                            }
                        }
                    }
                    connection.Close();
                }
                return new ReportViewModel()
                {
                    Transactions = transactions,
                    TotalExpenses = transactions.Where(t => t.Amount < 0).Sum(t => t.Amount),
                    TotalIncomes = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    categoryID = categoryID
                };

            }
        }



        [HttpPost]
        public ActionResult ViewReport(int categoryID)
        {
            ReportViewModel reportViewModel = getTransactionsForReport(categoryID);
            Console.WriteLine("Reportviewmodel:" + reportViewModel.categoryID);

            return View("ViewReport", getTransactionsForReport(categoryID));
        }

        public IActionResult DownloadTransactionsAsJson(int categoryID)
        {
            var reportViewModel = getTransactionsForReport(categoryID);
            var json = JsonSerializer.Serialize(reportViewModel);

            var bytes = Encoding.UTF8.GetBytes(json);
            var result = new FileContentResult(bytes, "application/json")
            {
                FileDownloadName = "transactions.json"
            };

            return result;
        }

    }

}


