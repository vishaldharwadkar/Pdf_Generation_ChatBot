using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;

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
    }
}
