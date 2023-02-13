using System;
using GitHub.DistributedTask.WebApi;
using GitHub.Runner.Common;

namespace GitHub.Runner.Worker
{
    public class FeatureManager
    {
        public static bool IsContainerHooksEnabled(Variables variables)
        {
            var isContainerHookFeatureFlagSet = variables?.GetBoolean(Constants.Runner.Features.AllowRunnerContainerHooks, SecretScope.Final) ?? false;
            var isContainerHooksPathSet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.Hooks.ContainerHooksPath));
            return isContainerHookFeatureFlagSet && isContainerHooksPathSet;
        }
    }
}
