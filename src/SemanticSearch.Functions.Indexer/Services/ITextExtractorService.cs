namespace SemanticSearch.Functions.Indexer.Services;

public interface ITextExtractorService
{
    string Extract(byte[] content, string filename);
}
