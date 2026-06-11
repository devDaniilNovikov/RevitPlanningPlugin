using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPlanningPlugin.Services.Logging;
using RevitPlanningPlugin.UI.Views;

namespace RevitPlanningPlugin.Commands
{
    /// <summary>
    /// Основная команда плагина: открывает окно генератора планировок.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenPlanningGeneratorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                PluginLogger.Initialize();
                PluginLogger.Info("Запуск команды генератора планировок.");

                var window = new MainWindow(commandData);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Критическая ошибка команды", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
