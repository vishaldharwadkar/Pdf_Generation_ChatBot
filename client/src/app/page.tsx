'use client';
import { useCallback, useRef, useState } from "react";
import { FaUpload } from "react-icons/fa";

export default function Home() {
  // Track uploaded PDFs and their extraction status
  const [pdfs, setPdfs] = useState<{
    name: string;
    filePath: string;
    status: 'Not Started' | 'In Progress' | 'Completed';
    pdfId?: string;
  }[]>([]);
  const [extractingIndex, setExtractingIndex] = useState<number | null>(null);
  const [selectedPdfIndex, setSelectedPdfIndex] = useState<number | null>(null);
  const [chatInput, setChatInput] = useState("");
  const [chatHistory, setChatHistory] = useState<{ question: string; answer: string }[]>([]);
  const [isAsking, setIsAsking] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Handle file selection from the normal button
  const handleFileChange = useCallback(async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0 || !files[0]) {
      return; // Nothing to do if no file is selected
    }
    const fileName = files[0].name; // Store name before async/clearing
    const formData = new FormData();
    formData.append('formFile', files[0]);
    const data = await fetch('https://localhost:7046/api/upload', {
      method: 'POST',
      body: formData
    });
    const result = await data.json();
    setPdfs(prev => [
      ...prev,
      {
        name: fileName,
        filePath: result.filePath,
        status: 'Not Started',
      }
    ]);
  }, []);

  // Simulate extraction API call
  const handleExtract = async (index: number) => {
    setExtractingIndex(index);
    setPdfs(prev => prev.map((pdf, i) => i === index ? { ...pdf, status: 'In Progress' } : pdf));
    // Call your backend extraction endpoint (replace URL as needed)
    const res = await fetch('https://localhost:7046/api/extract', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filePath: pdfs[index].filePath })
    });
    const result = await res.json();
    // Assume result.pdfId is returned after embedding is stored
    setPdfs(prev => prev.map((pdf, i) =>
      i === index ? { ...pdf, status: 'Completed', pdfId: result.pdfId } : pdf
    ));
    setExtractingIndex(null);
  };

  // Handle PDF selection
  const handleSelectPdf = (index: number) => {
    setSelectedPdfIndex(index);
    setChatHistory([]); // Reset chat when switching PDFs
  };

  // Handle chat input change
  const handleChatInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setChatInput(e.target.value);
  };

  // Handle chat form submit
  const handleChatSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedPdfIndex === null || !chatInput.trim()) return;
    setIsAsking(true);
    const pdfId = pdfs[selectedPdfIndex].pdfId;
    const question = chatInput.trim();
    setChatHistory(prev => [...prev, { question, answer: "..." }]);
    setChatInput("");
    try {
      const res = await fetch('https://localhost:7046/api/askQuestions', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pdfId, question })
      });
      const data = await res.json();
      setChatHistory(prev => prev.map((item, idx) =>
        idx === prev.length - 1 ? { ...item, answer: data.answer } : item
      ));
    } catch (err) {
      setChatHistory(prev => prev.map((item, idx) =>
        idx === prev.length - 1 ? { ...item, answer: 'Error getting answer.' } : item
      ));
    }
    setIsAsking(false);
  };

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100 p-4">
      <main className="w-full max-w-6xl flex flex-row bg-white shadow-md rounded-lg p-0 min-h-[500px]">
        {/* PDF Table & Upload - Left Side (40%) */}
        <section className="w-2/5 border-r border-gray-200 p-8 flex flex-col items-start justify-start">
          <div className="w-full flex items-center justify-between mb-4">
            <h3 className="font-semibold text-lg">Uploaded PDFs</h3>
            <button
              className="flex items-center gap-2 bg-blue-500 text-white px-4 py-2 rounded hover:bg-blue-600 focus:outline-none"
              onClick={() => fileInputRef.current?.click()}
            >
              <span>Upload PDF</span>
              <FaUpload />
            </button>
            <input
              type="file"
              accept="application/pdf"
              ref={fileInputRef}
              style={{ display: 'none' }}
              onChange={handleFileChange}
            />
          </div>
          <table className="w-full text-sm border border-gray-200 rounded-lg overflow-hidden bg-white shadow-sm">
            <thead>
              <tr className="bg-blue-500">
                <th className="text-left px-3 py-2 text-white font-semibold">PDF Name</th>
                <th className="text-left px-3 py-2 text-white font-semibold">Status</th>
                <th className="text-left px-3 py-2 text-white font-semibold">Action</th>
              </tr>
            </thead>
            <tbody>
              {pdfs.length === 0 ? (
                <tr>
                  <td colSpan={3} className="text-center py-6 text-gray-400">No PDFs uploaded yet.</td>
                </tr>
              ) : (
                pdfs.map((pdf, idx) => (
                  <tr key={idx} className={`border-t hover:bg-blue-50 transition-colors ${selectedPdfIndex === idx ? 'bg-blue-100' : ''}`}
                    onClick={() => handleSelectPdf(idx)}
                    style={{ cursor: 'pointer' }}
                  >
                    <td className="px-3 py-2 truncate max-w-[160px] text-gray-900 font-medium" title={pdf.name}>{pdf.name}</td>
                    <td className="px-3 py-2">
                      <span className={
                        pdf.status === 'Completed' ? 'text-green-600' :
                        pdf.status === 'In Progress' ? 'text-yellow-600' : 'text-gray-500'
                      }>{pdf.status}</span>
                    </td>
                    <td className="px-3 py-2">
                      <button
                        className="px-2 py-1 rounded-md bg-blue-500 text-white text-xs font-medium shadow-sm hover:bg-blue-600 transition disabled:opacity-50"
                        style={{ minWidth: 70 }}
                        disabled={pdf.status !== 'Not Started' || extractingIndex !== null}
                        onClick={e => { e.stopPropagation(); handleExtract(idx); }}
                      >
                        Extract
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </section>
        {/* Chatbot - Right Side (60%) */}
        <section className="w-3/5 p-8 flex flex-col justify-between min-h-[500px]">
          <div className="flex-1 overflow-y-auto mb-4">
            {selectedPdfIndex === null ? (
              <div className="text-gray-500 text-center mt-20">Select a PDF to start chatting.</div>
            ) : (
              <div className="flex flex-col gap-4">
                {chatHistory.length === 0 ? (
                  <div className="text-gray-400 text-center mt-20">No conversation yet. Ask a question about the selected PDF.</div>
                ) : (
                  chatHistory.map((msg, idx) => (
                    <div key={idx} className="">
                      <div className="font-semibold text-blue-700">You: <span className="font-normal text-gray-900">{msg.question}</span></div>
                      <div className="ml-4 text-gray-800">Bot: {msg.answer}</div>
                    </div>
                  ))
                )}
              </div>
            )}
          </div>
          <form className="flex gap-2" onSubmit={handleChatSubmit}>
            <input
              type="text"
              className="flex-1 text-black border border-gray-900 rounded-lg px-4 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder={selectedPdfIndex === null ? "Select a PDF to chat..." : "Type your message..."}
              value={chatInput}
              onChange={handleChatInputChange}
              disabled={selectedPdfIndex === null || isAsking}
            />
            <button
              type="submit"
              className="bg-blue-500 text-white px-4 py-2 rounded-lg hover:bg-blue-600 disabled:opacity-50"
              disabled={selectedPdfIndex === null || isAsking || !chatInput.trim()}
            >
              {isAsking ? 'Sending...' : 'Send'}
            </button>
          </form>
        </section>
      </main>
    </div>
  );
}
