import { useState } from 'react'
import './App.css'

interface AskResponse {
  answer: string
  sources: string[]
}

function App() {
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState<string | null>(null)
  const [sources, setSources] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canAsk = question.trim().length > 0 && !loading

  async function handleAsk() {
    setLoading(true)
    setError(null)
    setAnswer(null)
    setSources([])

    try {
      const response = await fetch('/api/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question }),
      })

      if (!response.ok) {
        throw new Error(`Request failed (${response.status})`)
      }

      const data: AskResponse = await response.json()
      setAnswer(data.answer)
      setSources(data.sources)
    } catch (err) {
      setError(
        err instanceof Error
          ? `Could not reach PolicyLens: ${err.message}`
          : 'Could not reach PolicyLens.',
      )
    } finally {
      setLoading(false)
    }
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault()
      if (canAsk) {
        void handleAsk()
      }
    }
  }

  return (
    <div className="app">
      <h1>PolicyLens</h1>
      <p className="subtitle">
        Ask a question about the Employee Remote Work &amp; Security Policy.
      </p>

      <textarea
        className="question-input"
        placeholder="e.g. How many days can I work from another country?"
        value={question}
        onChange={(event) => setQuestion(event.target.value)}
        onKeyDown={handleKeyDown}
        rows={3}
      />

      <button className="ask-button" onClick={() => void handleAsk()} disabled={!canAsk}>
        {loading ? 'Asking…' : 'Ask'}
      </button>

      {error && <div className="error-box">{error}</div>}

      {answer && (
        <div className="answer-box">
          <h2>Answer</h2>
          <p>{answer}</p>
        </div>
      )}

      {sources.length > 0 && (
        <div className="sources-box">
          <h2>Sources Used</h2>
          <ul>
            {sources.map((source) => (
              <li key={source}>{source}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

export default App
