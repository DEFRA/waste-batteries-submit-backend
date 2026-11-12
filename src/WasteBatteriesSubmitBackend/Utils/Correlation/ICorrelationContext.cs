namespace WasteBatteriesSubmitBackend.Utils.Correlation;

// The correlation id for the current request. One scope returns the same id.
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
