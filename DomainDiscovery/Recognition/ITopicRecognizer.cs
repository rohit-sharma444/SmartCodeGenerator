using DomainDiscovery.Model;

namespace DomainDiscovery.Recognition;

public interface ITopicRecognizer
{
    TopicResult Recognize(IEnumerable<string> seedEntityNames);
}
