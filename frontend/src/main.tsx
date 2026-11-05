import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import './style.css'

async function start() {
  if (import.meta.env.DEV && import.meta.env.MODE === 'mock') {
    const { installMock } = await import('./api/mock')
    installMock()
  }
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </React.StrictMode>
  )
}

start()
