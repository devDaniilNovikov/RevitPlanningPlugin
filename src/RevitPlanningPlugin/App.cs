using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin
{
    /// <summary>
    /// Точка входа Revit Add-in.
    /// Регистрирует вкладку Ribbon, панель и кнопки команд.
    /// </summary>
    public class App : IExternalApplication
    {
        private const string TabName = "Планировки";
        private const string PanelName = "Генератор";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                PluginLogger.Initialize();
                PluginLogger.Info("Инициализация плагина генерации планировок.");

                // Создаём собственную вкладку в Ribbon
                try { application.CreateRibbonTab(TabName); }
                catch { /* вкладка уже существует */ }

                var panel = application.CreateRibbonPanel(TabName, PanelName);
                var assemblyPath = Assembly.GetExecutingAssembly().Location;

                // Кнопка «Генератор планировок»
                var mainButton = new PushButtonData(
                    "PlanningGenerator",
                    "Генератор\nпланировок",
                    assemblyPath,
                    "RevitPlanningPlugin.Commands.OpenPlanningGeneratorCommand")
                {
                    ToolTip = "Получить контуры из API, сгенерировать варианты планировок и применить в модель.",
                    LongDescription = "Плагин позволяет загрузить контуры здания из внешнего сервиса, " +
                                      "запустить генерацию планировочных решений, сравнить варианты по метрикам " +
                                      "и применить выбранный вариант в текущий проект Revit."
                };

                // Иконки (опционально)
                try
                {
                    var iconDir = Path.GetDirectoryName(assemblyPath) ?? "";
                    var icon32 = Path.Combine(iconDir, "Resources", "icon_32.png");
                    var icon16 = Path.Combine(iconDir, "Resources", "icon_16.png");

                    if (File.Exists(icon32))
                        mainButton.LargeImage = new BitmapImage(new Uri(icon32));
                    if (File.Exists(icon16))
                        mainButton.Image = new BitmapImage(new Uri(icon16));
                }
                catch
                {
                    // Иконки не критичны
                }

                panel.AddItem(mainButton);

                PluginLogger.Info("Плагин успешно инициализирован.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Ошибка инициализации плагина", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            PluginLogger.Info("Завершение работы плагина.");
            return Result.Succeeded;
        }
    }
}
