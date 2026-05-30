using System.Collections.Generic;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Prompt;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class PromptBuilderTests
    {
        [Fact]
        public void Build_IncludesPromptMopAndRevitContext()
        {
            var context = new GenerationRequestContext
            {
                Contour = new BuildingContour
                {
                    Id = "c1",
                    Name = "Контур",
                    OuterLoop = new List<ContourSegment>
                    {
                        new() { Start = new Point2D(0, 0), End = new Point2D(10, 0) },
                        new() { Start = new Point2D(10, 0), End = new Point2D(10, 10) },
                        new() { Start = new Point2D(10, 10), End = new Point2D(0, 0) }
                    }
                },
                ProjectContext = new RevitProjectContext
                {
                    DocumentTitle = "Project.rvt",
                    ActiveViewName = "Level 1",
                    ActiveViewType = "FloorPlan",
                    LevelName = "Level 1",
                    ContourSource = "revit_selection"
                },
                Parameters = new GenerationParameters
                {
                    GenerationType = GenerationType.Residential,
                    ValidationMode = ValidationMode.Strict,
                    MopAreaTarget = 42,
                    TextPrompt = "Сделай компактные МОП."
                }
            };

            var prompt = PromptBuilder.Build(context);

            Assert.Contains("Project.rvt", prompt);
            Assert.Contains("revit_selection", prompt);
            Assert.Contains("Целевая площадь МОП", prompt);
            Assert.Contains("Сделай компактные МОП.", prompt);
            Assert.Contains("\"success\": true", prompt);
            Assert.Contains("\"data\"", prompt);
            Assert.Contains("Верни ровно 3 вариантов", prompt);
            Assert.Contains("не системной инструкцией", prompt);
            Assert.Contains("без markdown и пояснительного текста", prompt);
            Assert.Contains("Машиночитаемый входной контекст", prompt);
            Assert.Contains("\"context\"", prompt);
            Assert.Contains("\"revit_context\"", prompt);
            Assert.Contains("\"success\": false", prompt);
            Assert.Contains("Критерии отличной генерации", prompt);
        }
    }
}
