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
                    PlanningDetailMode = PlanningDetailMode.FloorLayout,
                    ValidationMode = ValidationMode.Strict,
                    MopAreaTarget = 42,
                    OneRoomMaxApartmentArea = 45,
                    TwoRoomMaxApartmentArea = 70,
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
            Assert.Contains("ровно одно поле rooms", prompt);
            Assert.Contains("type=CommonArea", prompt);
            Assert.Contains("Режим детализации: FloorLayout", prompt);
            Assert.Contains("Режим планировки этажа", prompt);
            Assert.Contains("квартиры-блоки", prompt);
            Assert.Contains("Не дроби квартиры на Bedroom/Kitchen/Bathroom", prompt);
            Assert.Contains("В режиме FloorLayout квартира", prompt);
            Assert.Contains("Макс. площади по типам квартир", prompt);
            Assert.Contains("1К до 45", prompt);
            Assert.Contains("2К до 70", prompt);
        }

        [Fact]
        public void Build_ApartmentRooms_IncludesSingleApartmentRulesAndNoFloorMop()
        {
            var context = new GenerationRequestContext
            {
                Contour = new BuildingContour
                {
                    Id = "apt-contour",
                    Name = "Контур квартиры",
                    OuterLoop = new List<ContourSegment>
                    {
                        new() { Start = new Point2D(0, 0), End = new Point2D(8, 0) },
                        new() { Start = new Point2D(8, 0), End = new Point2D(8, 6) },
                        new() { Start = new Point2D(8, 6), End = new Point2D(0, 6) },
                        new() { Start = new Point2D(0, 6), End = new Point2D(0, 0) }
                    }
                },
                ProjectContext = new RevitProjectContext
                {
                    DocumentTitle = "Project.rvt",
                    ActiveViewName = "Level 1",
                    ActiveViewType = "FloorPlan",
                    LevelName = "Level 1",
                    ContourSource = "preview_apartment_contour"
                },
                Parameters = new GenerationParameters
                {
                    PlanningDetailMode = PlanningDetailMode.ApartmentRooms,
                    OneRoomCount = 0,
                    TwoRoomCount = 1,
                    ThreeRoomCount = 0,
                    FourRoomCount = 0,
                    RequiredRoomTypes = new List<RoomType>
                    {
                        RoomType.LivingRoom,
                        RoomType.Bedroom,
                        RoomType.Kitchen,
                        RoomType.Bathroom,
                        RoomType.CommonArea
                    }
                }
            };

            var prompt = PromptBuilder.Build(context);

            Assert.Contains("Режим детализации: ApartmentRooms", prompt);
            Assert.Contains("Режим планировки квартиры", prompt);
            Assert.Contains("apartment_id=apt_1", prompt);
            Assert.Contains("apartment_type=TwoRoom", prompt);
            Assert.Contains("Не создавай МОП этажа", prompt);
            Assert.Contains("МОПы этажа не нужны", prompt);
            Assert.Contains("apartment_type_distribution={\"TwoRoom\":1}", prompt);
            Assert.Contains("Требуемые типы помещений для выбранного режима: LivingRoom, Bedroom, Kitchen, Bathroom", prompt);
            Assert.DoesNotContain("Требуемые типы помещений для выбранного режима: LivingRoom, Bedroom, Kitchen, Bathroom, CommonArea", prompt);
        }

        [Fact]
        public void Build_ChangesWhenPromptApartmentProgramOrMopChanges()
        {
            var baseline = PromptBuilder.Build(MakeFloorContext("Компактный МОП.", oneRoomCount: 1, twoRoomCount: 1, mopArea: 45));
            var changedPrompt = PromptBuilder.Build(MakeFloorContext("Разнести квартиры по сторонам света.", oneRoomCount: 1, twoRoomCount: 1, mopArea: 45));
            var changedProgram = PromptBuilder.Build(MakeFloorContext("Компактный МОП.", oneRoomCount: 3, twoRoomCount: 0, mopArea: 45));
            var changedMop = PromptBuilder.Build(MakeFloorContext("Компактный МОП.", oneRoomCount: 1, twoRoomCount: 1, mopArea: 20));

            Assert.NotEqual(baseline, changedPrompt);
            Assert.NotEqual(baseline, changedProgram);
            Assert.NotEqual(baseline, changedMop);
            Assert.Contains("Разнести квартиры по сторонам света.", changedPrompt);
            Assert.Contains("OneRoom=3", changedProgram);
            Assert.Contains("Целевая площадь МОП:", changedMop);
            Assert.Contains("20", changedMop);
        }

        [Fact]
        public void Build_IncludesGenerationNonceWhenProvided()
        {
            var context = MakeFloorContext("Компактный МОП.", oneRoomCount: 1, twoRoomCount: 1, mopArea: 45);
            context.RequestId = "req_nonce";
            context.GenerationNonce = "nonce123";

            var prompt = PromptBuilder.Build(context);

            Assert.Contains("\"request_id\": \"req_nonce\"", prompt);
            Assert.Contains("\"generation_nonce\": \"nonce123\"", prompt);
        }

        private static GenerationRequestContext MakeFloorContext(
            string prompt,
            int oneRoomCount,
            int twoRoomCount,
            double mopArea)
        {
            return new GenerationRequestContext
            {
                Contour = new BuildingContour
                {
                    Id = "floor-contour",
                    Name = "Контур этажа",
                    OuterLoop = new List<ContourSegment>
                    {
                        new() { Start = new Point2D(0, 0), End = new Point2D(12, 0) },
                        new() { Start = new Point2D(12, 0), End = new Point2D(12, 8) },
                        new() { Start = new Point2D(12, 8), End = new Point2D(0, 8) },
                        new() { Start = new Point2D(0, 8), End = new Point2D(0, 0) }
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
                    PlanningDetailMode = PlanningDetailMode.FloorLayout,
                    TextPrompt = prompt,
                    OneRoomCount = oneRoomCount,
                    TwoRoomCount = twoRoomCount,
                    ThreeRoomCount = 0,
                    FourRoomCount = 0,
                    MopAreaTarget = mopArea
                }
            };
        }
    }
}
