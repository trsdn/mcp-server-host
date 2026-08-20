using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HostMcp.ComInterop;

/// <summary>
/// Minimal late-binding wrapper around an <c>IDispatch</c> COM object.
/// </summary>
/// <remarks>
/// <para>
/// Emulator automation objects are accessed through late binding on purpose. Referencing the
/// PCOMM interop assemblies would force every consumer to have the exact PCOMM version installed
/// at build time, and would break as soon as IBM ships a new type library. Late binding through
/// <see cref="Type.InvokeMember(string, BindingFlags, Binder, object, object[])"/> keeps the build
/// completely independent of any installed emulator, which is also what makes CI possible.
/// </para>
/// </remarks>
public sealed class ComObject : IDisposable
{
    private object? _instance;
    private readonly bool _ownsInstance;

    private ComObject(object instance, bool ownsInstance)
    {
        _instance = instance;
        _ownsInstance = ownsInstance;
    }

    /// <summary>Gets the wrapped COM instance.</summary>
    public object Instance => _instance ?? throw new ObjectDisposedException(nameof(ComObject));

    /// <summary>
    /// Creates a COM object from a ProgID.
    /// </summary>
    /// <param name="progId">ProgID, for example <c>PCOMM.autECLSession</c>.</param>
    /// <returns>The created object, or null when the ProgID is not registered.</returns>
    public static ComObject? TryCreate(string progId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var type = Type.GetTypeFromProgID(progId, throwOnError: false);
            if (type is null)
            {
                return null;
            }

            var instance = Activator.CreateInstance(type);
            return instance is null ? null : new ComObject(instance, ownsInstance: true);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (TypeLoadException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a COM object from a ProgID and throws a descriptive error when it is not registered.
    /// </summary>
    /// <param name="progId">ProgID, for example <c>PCOMM.autECLSession</c>.</param>
    /// <returns>The created object.</returns>
    /// <exception cref="InvalidOperationException">The ProgID is not registered for this process bitness.</exception>
    public static ComObject Create(string progId)
        => TryCreate(progId) ?? throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"COM ProgID '{progId}' is not registered for a {(Environment.Is64BitProcess ? "64" : "32")}-bit process. IBM Personal Communications usually registers its automation objects as 32-bit; run the {(Environment.Is64BitProcess ? "win-x86" : "win-x64")} build instead. See docs/PLATFORM.md."));

    /// <summary>Wraps an existing COM instance without taking ownership of its lifetime.</summary>
    /// <param name="instance">COM instance.</param>
    /// <returns>The wrapper.</returns>
    public static ComObject Wrap(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new ComObject(instance, ownsInstance: false);
    }

    /// <summary>Reads a property value.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The property value.</returns>
    public object? Get(string name) => Invoke(name, BindingFlags.GetProperty, []);

    /// <summary>Reads a property value and converts it to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="name">Property name.</param>
    /// <returns>The converted property value.</returns>
    public T GetValue<T>(string name) => Convert<T>(Get(name));

    /// <summary>Reads a property that returns a COM object.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The wrapped child object.</returns>
    public ComObject GetObject(string name)
        => Get(name) is { } child
            ? Wrap(child)
            : throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"COM property '{name}' returned null."));

    /// <summary>Sets a property value.</summary>
    /// <param name="name">Property name.</param>
    /// <param name="value">New value.</param>
    public void Set(string name, object? value) => Invoke(name, BindingFlags.SetProperty, [value]);

    /// <summary>Calls a method.</summary>
    /// <param name="name">Method name.</param>
    /// <param name="args">Arguments.</param>
    /// <returns>The method result.</returns>
    public object? Call(string name, params object?[] args) => Invoke(name, BindingFlags.InvokeMethod, args);

    /// <summary>Calls a method and converts the result to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="name">Method name.</param>
    /// <param name="args">Arguments.</param>
    /// <returns>The converted method result.</returns>
    public T CallValue<T>(string name, params object?[] args) => Convert<T>(Call(name, args));

    /// <summary>Calls a method that returns a COM object.</summary>
    /// <param name="name">Method name.</param>
    /// <param name="args">Arguments.</param>
    /// <returns>The wrapped result object.</returns>
    public ComObject CallObject(string name, params object?[] args)
        => Call(name, args) is { } child
            ? Wrap(child)
            : throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"COM method '{name}' returned null."));

    /// <inheritdoc />
    public void Dispose()
    {
        var instance = _instance;
        _instance = null;

        if (instance is null || !_ownsInstance)
        {
            return;
        }

        if (OperatingSystem.IsWindows() && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    private object? Invoke(string name, BindingFlags flags, object?[] args)
    {
        var instance = Instance;
        return instance.GetType().InvokeMember(
            name,
            flags,
            binder: null,
            target: instance,
            args: args,
            culture: CultureInfo.InvariantCulture);
    }

    private static T Convert<T>(object? value)
    {
        if (value is null)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }
}
