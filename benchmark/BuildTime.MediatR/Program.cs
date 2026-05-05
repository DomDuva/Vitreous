using BuildTime.MediatRApp;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

// Configure DI — this is what we're building.
// The app exits immediately after so it doesn't run during the build-time test.
var services = new ServiceCollection();
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetItemQuery>());
_ = services.BuildServiceProvider();
