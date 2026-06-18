namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class AutomationRuntime
{
    private readonly AutomationConfig _config;
    private readonly Func<FlowStep, CancellationToken, Task> _executeStepAsync;
    private readonly Func<double, CancellationToken, Task> _delayAsync;
    private readonly Func<double, CancellationToken, Task> _variableDelayAsync;
    private readonly Func<string, Task> _logAsync;

    public AutomationRuntime(
        AutomationConfig config,
        Func<FlowStep, CancellationToken, Task> executeStepAsync,
        Func<double, CancellationToken, Task> delayAsync,
        Func<double, CancellationToken, Task> variableDelayAsync,
        Func<string, Task> logAsync)
    {
        _config = config;
        _executeStepAsync = executeStepAsync;
        _delayAsync = delayAsync;
        _variableDelayAsync = variableDelayAsync;
        _logAsync = logAsync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _logAsync("自动流程：启动缓冲。");
        await _variableDelayAsync(_config.StartDelaySeconds, cancellationToken);

        var round = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            await _logAsync($"自动流程：第 {round} 轮开始。");
            foreach (var step in CurrencyWarsFlow.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _logAsync($"自动流程：执行 {step.Name}");
                await _executeStepAsync(step, cancellationToken);

                if (step.WaitAfterSeconds > 0)
                {
                    await _logAsync($"自动流程：{step.Name} 后等待 {step.WaitAfterSeconds:g} 秒。");
                    if (step.FixedWaitAfter)
                    {
                        await _delayAsync(step.WaitAfterSeconds, cancellationToken);
                    }
                    else
                    {
                        await _variableDelayAsync(step.WaitAfterSeconds, cancellationToken);
                    }
                }

                if (step.StandardDelayAfterSeconds > 0)
                {
                    await _logAsync($"自动流程：{step.Name} 标准步骤间隔 {step.StandardDelayAfterSeconds:g} 秒。");
                    await _variableDelayAsync(step.StandardDelayAfterSeconds, cancellationToken);
                }
            }

            await _logAsync($"自动流程：第 {round} 轮完成，回到第 1 步继续刷新。");
            round++;
        }
    }
}
