// The car list. All makes and models are fictional. Performance figures are
// measured by the web version's tests/physics.test.ts and recorded in
// docs/CARS.md. Ported from src/vehicles/cars.ts with identical values;
// dotnet/Halcyon.Core.Tests checks every field against the TypeScript.
using System;

namespace Halcyon.Core
{
    public static class Cars
    {
        /// <summary>CARS.</summary>
        public static readonly CarSpec[] All =
        {
            new CarSpec
            {
                id = "halden-aster-gt",
                make = "Halden",
                model = "Aster GT",
                year = 2024,
                category = Category.Sports,
                body = BodyType.Coupe,
                description = "Front-engined, rear-drive coupe with a twin-turbo straight six. Balanced and quick, with a rear end that rewards a careful right foot.",
                mass = 1490,
                weightFront = 0.52,
                wheelbase = 2.47,
                trackFront = 1.59,
                trackRear = 1.6,
                cgHeight = 0.46,
                length = 4.38,
                width = 1.86,
                height = 1.3,
                groundClearance = 0.12,
                engine = new EngineSpec
                {
                    layout = "I6",
                    cylinders = 6,
                    displacement = 3.0,
                    aspiration = Aspiration.TwinTurbo,
                    torque = new (double rpm, double nm)[] { (800, 230), (1500, 420), (1800, 500), (4600, 500), (5500, 478), (6500, 420), (7100, 360) },
                    idle = 800,
                    redline = 7000,
                    limiter = 7200,
                    inertia = 0.2,
                    turboLag = 0.35,
                },
                drivetrain = Drivetrain.RWD,
                diff = new DiffSpec { type = DiffType.LSD, preload = 80, accel = 0.35, decel = 0.2 },
                gears = new[] { 3.32, 2.13, 1.53, 1.21, 1.0, 0.82 },
                final = 3.46,
                reverse = 3.2,
                shiftTime = 0.2,
                tires = new TireSpec { compound = Compound.Sport, widthF = 255, widthR = 275, radius = 0.335 },
                brakes = new BrakeSpec { torqueF = 3000, torqueR = 1700 },
                suspension = new SuspensionSpec { freqF = 1.9, freqR = 2.0, dampF = 0.33, dampR = 0.33, arbF = 26000, arbR = 14000, travel = 0.16 },
                aero = new AeroSpec { cdA = 0.66, clA = 0.12, balance = 0.45 },
                steering = new SteeringSpec { maxAngle = 34, ratio = 13.5 },
                visual = new CarVisual { paint = 0xc7262e, style = "coupe" },
            },
        };

        /// <summary>carById(id). Throws for an unknown id.</summary>
        public static CarSpec ById(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].id == id) return All[i];
            }
            throw new ArgumentException($"unknown car {id}", nameof(id));
        }
    }
}
