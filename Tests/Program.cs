
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "RaceImperialis.slnx"))) root = root.Parent;
if (root == null) throw new DirectoryNotFoundException("Could not find the Race Imperialis repository root.");
var checks = await RacingProjectChecks.RunAsync(root.FullName);
checks += await GraphicsOptionsChecks.RunAsync();
checks += NativeRuntimeChecks.Run(root.FullName);
Console.WriteLine($"PASS: {checks} Race Imperialis assertions (assets, countdown, difficulties, driving, pause, restart, finishing, and graphics preferences).");
