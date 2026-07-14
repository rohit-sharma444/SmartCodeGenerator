using DomainDiscovery.Model;

namespace DomainDiscovery.Recognition;

public interface ITopicRecognizer
{
    /// <param name="seedEntityNames">The entity names NLE found in the user's text.</param>
    /// <param name="description">
    /// The user's text itself, when the caller has it. The seed names are single words, and the
    /// domain is usually named by several of them together ("point of sale"), which no seed name
    /// carries on its own — see <see cref="WikipediaTopicRecognizer"/>.
    /// </param>
    TopicResult Recognize(IEnumerable<string> seedEntityNames, string? description = null);
}
