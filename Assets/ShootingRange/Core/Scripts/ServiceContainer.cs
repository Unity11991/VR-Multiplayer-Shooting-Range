using System;
using System.Collections.Generic;

namespace ShootingRange.Core
{
    /// <summary>
    /// Explicit service container for composition root dependency injection.
    /// Does not rely on persistent static state.
    /// </summary>
    public sealed class ServiceContainer
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<T>(T implementation) where T : class
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services[type] = implementation;
            }
            else
            {
                _services.Add(type, implementation);
            }
        }

        public T Resolve<T>() where T : class
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return (T)service;
            }
            throw new InvalidOperationException($"Service of type {type.Name} is not registered in this ServiceContainer.");
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var obj))
            {
                service = (T)obj;
                return true;
            }
            service = null;
            return false;
        }

        public void Unregister<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        public void Clear()
        {
            _services.Clear();
        }
    }
}

