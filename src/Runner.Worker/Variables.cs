using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GitHub.DistributedTask.Logging;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.WebApi;
using GitHub.Runner.Common;
using GitHub.Runner.Common.Util;
using GitHub.Runner.Sdk;

namespace GitHub.Runner.Worker
{


    public sealed class Variables
    {
        private readonly IHostContext _hostContext;
        private readonly Dictionary<SecretScope, Dictionary<string, Variable>> _variables = new Dictionary<SecretScope, Dictionary<string, Variable>>{{SecretScope.Org, new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase)},
        {SecretScope.Repo, new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase)}, {SecretScope.Final, new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase)}};
        private readonly ISecretMasker _secretMasker;
        private readonly object _setLock = new();
        private readonly Tracing _trace;

        public IEnumerable<Variable> AllVariables
        {
            get
            {
                //TBD scope
                var output = new List<Variable>();
                foreach (var varScope in _variables){
                    output.AddRange(varScope.Value.Values);
                }
                return output;
            }
        }

        public Variables(IHostContext hostContext, IDictionary<SecretScope, IDictionary<string, VariableValue>> copy)
        {
            // Store/Validate args.
            _hostContext = hostContext;
            _secretMasker = _hostContext.SecretMasker;
            _trace = _hostContext.GetTrace(nameof(Variables));
            ArgUtil.NotNull(hostContext, nameof(hostContext));

            // Validate the dictionary, remove any variable with empty variable name.
            ArgUtil.NotNull(copy, nameof(copy));
            foreach (var varScope in copy){
                if (varScope.Value.Keys.Any(k => string.IsNullOrWhiteSpace(k)))
                {
                    _trace.Info($"Remove {varScope.Value.Keys.Count(k => string.IsNullOrWhiteSpace(k))} variables with empty variable name.");
                }
            }

            // Initialize the variable dictionary.
            Dictionary<SecretScope, List<Variable>> variables = new();
            foreach (var varScope in copy)
            {
                variables[varScope.Key] = new List<Variable>();
                foreach (var variable in varScope.Value)
                {
                    if (!string.IsNullOrWhiteSpace(variable.Key))
                    {
                        variables[varScope.Key].Add(new Variable(variable.Key, variable.Value.Value, variable.Value.IsSecret));
                    }
                }
            }

            foreach (var varScope in variables)
            {
                foreach (Variable variable in varScope.Value)
                {
                    // Store the variable. The initial secret values have already been
                    // registered by the Worker class.
                    _variables[varScope.Key][variable.Name] = variable;
                }
            }
        }

        // DO NOT add file path variable to here.
        // All file path variables needs to be retrive and set through ExecutionContext, so it can handle container file path translation.
        public string Build_Number => Get(SdkConstants.Variables.Build.BuildNumber, SecretScope.Final);

#if OS_WINDOWS
        public bool Retain_Default_Encoding => false;
#else
        public bool Retain_Default_Encoding => true;
#endif

        public bool? Step_Debug => GetBoolean(Constants.Variables.Actions.StepDebug, SecretScope.Final);

        public string System_PhaseDisplayName => Get(Constants.Variables.System.PhaseDisplayName, SecretScope.Final);

        public string Get(string name, SecretScope scope)
        {
            Variable variable;
            if (_variables[scope].TryGetValue(name, out variable))
            {
                _trace.Verbose($"Get '{name}': '{variable.Value}'");
                return variable.Value;
            }

            _trace.Verbose($"Get '{name}' (not found)");
            return null;
        }

        public bool? GetBoolean(string name, SecretScope scope)
        {
            bool val;
            if (bool.TryParse(Get(name, scope), out val))
            {
                return val;
            }

            return null;
        }

        public T? GetEnum<T>(string name) where T : struct
        {
            return EnumUtil.TryParse<T>(Get(name, SecretScope.Final));
        }

        public Guid? GetGuid(string name)
        {
            Guid val;
            if (Guid.TryParse(Get(name, SecretScope.Final), out val))
            {
                return val;
            }

            return null;
        }

        public int? GetInt(string name)
        {
            int val;
            if (int.TryParse(Get(name, SecretScope.Final), out val))
            {
                return val;
            }

            return null;
        }

        public long? GetLong(string name)
        {
            long val;
            if (long.TryParse(Get(name, SecretScope.Final), out val))
            {
                return val;
            }

            return null;
        }

        public void Set(string name, string val, SecretScope scope)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));
            _variables[scope][name] = new Variable(name, val, false);
        }

        public bool TryGetValue(string name, SecretScope scope, out string val)
        {
            Variable variable;
            if (_variables[scope].TryGetValue(name, out variable))
            {
                val = variable.Value;
                _trace.Verbose($"Get '{name}': '{val}'");
                return true;
            }

            val = null;
            _trace.Verbose($"Get '{name}' (not found)");
            return false;
        }

        public DictionaryContextData ToSecretsContext(SecretScope scope)
        {
            var result = new DictionaryContextData();

            foreach (var variable in _variables[scope].Values)
            {
                if (variable.Secret &&
                    !string.Equals(variable.Name, Constants.Variables.System.AccessToken, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(variable.Name, "system.github.token", StringComparison.OrdinalIgnoreCase))
                {
                    result[variable.Name] = new StringContextData(variable.Value);
                }
            }
            return result;
        }
    }

    public sealed class Variable
    {
        public string Name { get; private set; }
        public bool Secret { get; private set; }
        public string Value { get; private set; }

        public Variable(string name, string value, bool secret)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));
            Name = name;
            Value = value ?? string.Empty;
            Secret = secret;
        }
    }
}
