import type { StudentLabView } from './types'

type LabContentProps = {
  lab: StudentLabView
}

export default function LabContent({ lab }: LabContentProps) {
  const tasks = [...lab.tasks].sort((first, second) => first.order - second.order)

  return (
    <article>
      <h2>{lab.title}</h2>
      <p>{lab.goal}</p>

      {tasks.length === 0 && <p>Laboratorija trenutno nema zadataka.</p>}

      {tasks.map(task => (
        <section key={task.taskId} className="card">
          <h3>Zadatak {task.order}</h3>
          <p>{task.prompt}</p>

          {task.options.length > 0 && (
            <ul>
              {task.options.map((option, index) => (
                <li key={index}>{option}</li>
              ))}
            </ul>
          )}
        </section>
      ))}
    </article>
  )
}