import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import TitleBar from './TitleBar';
import './styles.css';
import './ink-indigo.css';
createRoot(document.getElementById('root')!).render(<React.StrictMode><TitleBar/><App/></React.StrictMode>);
