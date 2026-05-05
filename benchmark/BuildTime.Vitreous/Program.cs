using Microsoft.Extensions.DependencyInjection;
using Vitreous;

// Configure DI using the source-generated AddVitreousHandlers().
// The generator emits VitreousDispatcher.g.cs and
// VitreousServiceCollectionExtensions.g.cs at compile time.
// The app exits immediately after so it doesn't run during the build-time test.
var services = new ServiceCollection();
services.AddVitreousHandlers();
_ = services.BuildServiceProvider();
