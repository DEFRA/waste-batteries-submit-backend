namespace WasteBatteriesSubmitBackend.Utils.Actor;

// The caller's opaque id for an outbox event. Personal details stay out of the event.
public interface IActorContext
{
    string ActorId { get; }
}
