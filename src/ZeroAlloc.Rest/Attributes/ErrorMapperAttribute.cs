namespace ZeroAlloc.Rest.Attributes;

/// <summary>
/// Names the <see cref="IHttpErrorMapper{TError}"/> that turns an <see cref="HttpError"/> into the
/// error type of this interface's <c>Result&lt;T, TError&gt;</c> methods. Declare one per error type.
/// The generated <c>Add{I}</c> registers the mapper, and the client is built with the concrete
/// mapper type, so a host registration of <see cref="IHttpErrorMapper{TError}"/> never replaces it.
/// </summary>
/// <remarks>
/// The generator reports ZRA002 to ZRA004 as errors. <c>#pragma warning disable</c> does not
/// suppress them; fix the declaration.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
public sealed class ErrorMapperAttribute(Type mapperType) : Attribute
{
    /// <summary>
    /// The mapper: a closed, non-abstract class that implements <see cref="IHttpErrorMapper{TError}"/> and has
    /// a public constructor.
    /// </summary>
    public Type MapperType { get; } = mapperType;
}
