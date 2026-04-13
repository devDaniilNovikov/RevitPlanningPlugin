using System;
using Autodesk.Revit.DB;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.Revit.Transactions
{
    /// <summary>
    /// Безопасная обертка над Revit Transaction.
    /// Гарантирует корректный commit/rollback.
    /// </summary>
    public static class SafeTransaction
    {
        /// <summary>
        /// Выполняет действие внутри транзакции Revit.
        /// При исключении — автоматический rollback.
        /// </summary>
        public static void Execute(Document doc, string name, Action<Transaction> action)
        {
            using var tx = new Transaction(doc, name);
            try
            {
                tx.Start();
                action(tx);
                tx.Commit();
                PluginLogger.Debug($"Транзакция '{name}' выполнена.");
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                PluginLogger.Error($"Транзакция '{name}' откачена: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Выполняет действие внутри транзакции и возвращает результат.
        /// </summary>
        public static T Execute<T>(Document doc, string name, Func<Transaction, T> func)
        {
            using var tx = new Transaction(doc, name);
            try
            {
                tx.Start();
                var result = func(tx);
                tx.Commit();
                PluginLogger.Debug($"Транзакция '{name}' выполнена.");
                return result;
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                PluginLogger.Error($"Транзакция '{name}' откачена: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Группа транзакций (TransactionGroup) для атомарной операции из нескольких шагов.
        /// </summary>
        public static void ExecuteGroup(Document doc, string groupName, Action action)
        {
            using var group = new TransactionGroup(doc, groupName);
            try
            {
                group.Start();
                action();
                group.Assimilate();
            }
            catch (Exception ex)
            {
                if (group.HasStarted())
                    group.RollBack();
                PluginLogger.Error($"Группа транзакций '{groupName}' откачена: {ex.Message}", ex);
                throw;
            }
        }
    }
}
