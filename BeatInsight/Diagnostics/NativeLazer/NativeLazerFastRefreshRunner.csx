using BeatInsight.Diagnostics.NativeLazer;

return NativeLazerFastRefreshHarness.RunFromEnvironment(
    new NativeLazerClrGameRouteMethodTableResolver(),
    new NativeLazerClrGameplayStatsMethodTableResolver());
