using System;
using System.Collections.Generic;

namespace FluentFlyoutWPF.Classes.Services;

public sealed class SimpleServiceContainer
{
    private readonly Dictionary<Type, Func<SimpleServiceContainer, object>> _singletonFactories = [];
    private readonly Dictionary<Type, object> _singletons = [];
    private readonly Dictionary<Type, Func<SimpleServiceContainer, object>> _transientFactories = [];

    public void AddSingleton<TService>(Func<SimpleServiceContainer, TService> factory)
        where TService : class
    {
        _singletonFactories[typeof(TService)] = services => factory(services);
    }

    public void AddTransient<TService>(Func<SimpleServiceContainer, TService> factory)
        where TService : class
    {
        _transientFactories[typeof(TService)] = services => factory(services);
    }

    public TService GetRequiredService<TService>() where TService : class
    {
        var serviceType = typeof(TService);

        if (_singletons.TryGetValue(serviceType, out var existingSingleton))
        {
            return (TService)existingSingleton;
        }

        if (_singletonFactories.TryGetValue(serviceType, out var singletonFactory))
        {
            var created = (TService)singletonFactory(this);
            _singletons[serviceType] = created;
            return created;
        }

        if (_transientFactories.TryGetValue(serviceType, out var transientFactory))
        {
            return (TService)transientFactory(this);
        }

        throw new InvalidOperationException($"Service of type '{serviceType.FullName}' has not been registered.");
    }
}
