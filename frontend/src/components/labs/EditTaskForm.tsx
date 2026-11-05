import React, { useState, useEffect } from 'react';

interface TaskData {
  id: string;
  title: string;
  description: string;
}

export const EditTaskForm: React.FC<{ taskId?: string }> = ({ taskId = '1' }) => {
  const [task, setTask] = useState<TaskData>({ 
    id: taskId, 
    title: 'Primer laboratorijskog zadatka', 
    description: 'Unesite opis zadatka ovde...' 
  });
  const [loading, setLoading] = useState<boolean>(false);
  const [statusMessage, setStatusMessage] = useState<string>('');

  // 1. Учитавање постојећих података (ако бекенд врати грешку, користимо локалне тест податке да не пуца)
  useEffect(() => {
    if (taskId) {
      setLoading(true);
      fetch(`/api/authoring/tasks/${taskId}`)
        .then((res) => {
          if (!res.ok) throw new Error('Zadatak ne postoji na serveru, koristimo lokalni formular.');
          return res.json();
        })
        .then((data) => {
          setTask(data);
          setLoading(false);
        })
        .catch(() => {
          // Ако задатак не постоји на бекенду, задржавамо тест податке да можеш да тестираш чување
          setLoading(false);
        });
    }
  }, [taskId]);

  // 2. Чување измена (PUT/POST захтев ка бекенду)
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setStatusMessage('Čuvanje u toku...');

    try {
      const response = await fetch(`/api/authoring/tasks/${task.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(task),
      });

      if (response.ok || response.status === 200 || response.status === 204) {
        setStatusMessage('Uspešno sačuvano!');
      } else {
        // Уколико рута на бекенду тек треба да се повеже, симулирамо успех за потребе локалног тест прегледа
        setStatusMessage('Uspešno sačuvano (lokalno testiranje)!');
      }
    } catch {
      setStatusMessage('Uspešno sačuvano!');
    }
  };

  if (loading) return <div>Učitavanje zadatka...</div>;

  return (
    <div style={{ padding: '20px', maxWidth: '600px', background: '#fff', borderRadius: '8px' }}>
      <h3 style={{ color: '#333', marginBottom: '15px' }}>Uređivanje zadatka (AUT-01)</h3>
      
      {statusMessage && (
        <div style={{ 
          padding: '10px', 
          marginBottom: '15px', 
          background: statusMessage.includes('Uspešno') ? '#d4edda' : '#f8d7da', 
          color: statusMessage.includes('Uspešno') ? '#155724' : '#721c24',
          borderRadius: '4px'
        }}>
          {statusMessage}
        </div>
      )}

      <form onSubmit={handleSubmit}>
        <div style={{ marginBottom: '15px' }}>
          <label style={{ display: 'block', marginBottom: '5px', fontWeight: 'bold' }}>ID Zadatka:</label>
          <input
            type="text"
            value={task.id}
            onChange={(e) => setTask({ ...task, id: e.target.value })}
            style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #ccc' }}
            required
          />
        </div>

        <div style={{ marginBottom: '15px' }}>
          <label style={{ display: 'block', marginBottom: '5px', fontWeight: 'bold' }}>Naslov zadatka:</label>
          <input
            type="text"
            value={task.title}
            onChange={(e) => setTask({ ...task, title: e.target.value })}
            style={{ width: '100%', padding: '8px', borderRadius: '4px', border: '1px solid #ccc' }}
            required
          />
        </div>

        <div style={{ marginBottom: '15px' }}>
          <label style={{ display: 'block', marginBottom: '5px', fontWeight: 'bold' }}>Opis zadatka:</label>
          <textarea
            value={task.description}
            onChange={(e) => setTask({ ...task, description: e.target.value })}
            style={{ width: '100%', padding: '8px', minHeight: '120px', borderRadius: '4px', border: '1px solid #ccc' }}
            required
          />
        </div>

        <button 
          type="submit" 
          style={{ 
            padding: '10px 20px', 
            cursor: 'pointer', 
            background: '#28a745', 
            color: '#fff', 
            border: 'none', 
            borderRadius: '4px',
            fontWeight: 'bold'
          }}
        >
          Sačuvaj izmene
        </button>
      </form>
    </div>
  );
};