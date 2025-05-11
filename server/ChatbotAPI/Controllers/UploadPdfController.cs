using Microsoft.AspNetCore.Mvc;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;

namespace ChatbotAPI.Controllers
{
    [Route("api")]
    [ApiController]
    public class UploadPdfController : ControllerBase
    {
        public UploadPdfController()
        {

        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadPDF([FromForm] IFormFile formFile)
        {
            try
            {
                if (formFile == null || formFile.Length == 0)
                {
                    return BadRequest("No file uploaded.");
                }

                // Save to a source folder inside the project directory
                var sourceFolder = Path.Combine(Directory.GetCurrentDirectory(), "UploadedPdfs");
                if (!Directory.Exists(sourceFolder))
                {
                    Directory.CreateDirectory(sourceFolder);
                }

                // Use the original file name
                var fileName = Path.GetFileName(formFile.FileName);
                var filePath = Path.Combine(sourceFolder, fileName);

                // Save the file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await formFile.CopyToAsync(stream);
                }

                return Ok(new { message = "File uploaded successfully", filePath });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("extract")]
        public async Task<IActionResult> extract([FromBody] ExtractRequest request)
        {
            try
            {
                var filePath = request.filePath;
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound("PDF file not found.");
                }

                // Generate a unique PDF ID for this extraction
                var pdfId = Guid.NewGuid().ToString();

                var paragraphs = new List<string>();
                var text = new StringBuilder();
                await Task.Run(() =>
                {
                    using (var pdf = PdfDocument.Open(filePath))
                    {
                        foreach (var page in pdf.GetPages())
                        {
                            text.AppendLine(page.Text);
                        }
                    }
                });
                Console.WriteLine(text);

                var allParagraph = new List<string>();
                string fullText = text.ToString();
                int chunkSize = 500;
                for (int i = 0; i < fullText.Length; i += chunkSize)
                {
                    int length = Math.Min(chunkSize, fullText.Length - i);
                    allParagraph.Add(fullText.Substring(i, length));
                }

                // Embed and store chunks in Qdrant, pass pdfId
                await EmbedAndStoreChunksAsync(allParagraph, filePath, pdfId);

                return Ok(new { message = "File extracted, chunked, embedded, and stored in Qdrant successfully", filePath, pdfId, chunks = allParagraph });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return StatusCode(500, ex.Message);
            }
        }

        public class ExtractRequest
        {
            public string? filePath { get; set; }
        }

        private async Task<float[]> GetEmbeddingFromServerAsync(string text)
        {
            using (var httpClient = new HttpClient())
            {
                var requestBody = new { texts = new List<string> { text } };
                var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                // Change the port below to match your Python server's port (e.g., 8000)
                var response = await httpClient.PostAsync("http://localhost:8000/embed", content);  //python local host
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(responseString);
                var embeddingArray = doc.RootElement.GetProperty("embeddings")[0].EnumerateArray();
                var embedding = new List<float>();
                foreach (var v in embeddingArray)
                    embedding.Add(v.GetSingle());
                return embedding.ToArray();
            }
        }

        private async Task EmbedAndStoreChunksAsync(List<string> chunks, string filePath, string pdfId)
        {
            var qdrantClient = new QdrantClient("127.0.0.1", 6334);
            var collectionName = "pdf_chunks";
            var collections = await qdrantClient.CollectionExistsAsync(collectionName);
            if (!collections)
            {
                await qdrantClient.CreateCollectionAsync(
                    collectionName,
                    new Qdrant.Client.Grpc.VectorParams
                    {
                        Size = 768, // Set this to your embedding dimension (e.g., 768 for gte-base)
                        Distance = Qdrant.Client.Grpc.Distance.Cosine
                    }
                );
            }
            var points = new List<PointStruct>();

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var embedding = await GetEmbeddingFromServerAsync(chunk);
                ReadOnlyMemory<float> vector = new ReadOnlyMemory<float>(embedding);

                var id = (filePath + "_" + i).GetHashCode();
                var payload = new Dictionary<string, Value>
                {
                    { "text", new Value { StringValue = chunk } },
                    { "filePath", new Value { StringValue = filePath } },
                    { "chunkIndex", new Value { IntegerValue = i } },
                    { "pdfId", new Value { StringValue = pdfId } } // Add pdfId to payload
                };

                // Assign the vector array directly
                var vectors = new Vectors { Vector = new Vector { Data = { vector.ToArray() } } };

                var point = new PointStruct
                {
                    Id = new PointId { Num = (ulong)Math.Abs(id) },
                    Vectors = vectors,
                    Payload = { payload }
                };

                points.Add(point);
            }

            await qdrantClient.UpsertAsync("pdf_chunks", points);
        }

        [HttpPost("askQuestions")]
        public async Task<IActionResult> AskQuestions([FromBody] AskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.pdfId) || string.IsNullOrWhiteSpace(request.question))
                return BadRequest("pdfId and question are required.");

            // 1. Search Qdrant for relevant chunks by pdfId
            var qdrantClient = new QdrantClient("127.0.0.1", 6334);

            //can be refered as select query to 
            var filter = new Filter
            {
                Must = {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "pdfId",
                            Match = new Match { Text = request.pdfId }
                        }
                    }
                }
            };

            // (Optional) Get embedding for the question from your Python server
            var questionEmbedding = await GetEmbeddingFromServerAsync(request.question);

            // 2. Search Qdrant for similar chunks (using the embedding)
            var searchResult = await qdrantClient.SearchAsync(
                collectionName: "pdf_chunks",
                vector: questionEmbedding,
                filter: filter,
                limit: 5 // top 5 relevant chunks
            );

            // 3. Concatenate the most relevant chunks as context
            var context = string.Join("\n", searchResult.Select(r => r.Payload["text"].StringValue));

            // 4. Call your Python server to get the answer (replace with your actual endpoint)
            string answer = "No answer found.";
            using (var httpClient = new HttpClient())
            {
                var reqBody = JsonSerializer.Serialize(new { question = request.question, context });
                var content = new StringContent(reqBody, System.Text.Encoding.UTF8, "application/json");
                var resp = await httpClient.PostAsync("http://localhost:8000/answer", content);
                if (resp.IsSuccessStatusCode)
                {
                    var respStr = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respStr);
                    answer = doc.RootElement.GetProperty("answer").GetString() ?? answer;
                }
            }

            return Ok(new { answer });
        }

        public class AskRequest
        {
            public string? pdfId { get; set; }
            public string? question { get; set; }
        }
    }
}

