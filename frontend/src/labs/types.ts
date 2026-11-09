export type StudentTaskView = {
  taskId: string
  order: number
  prompt: string
  options: string[]
}

export type StudentLabView = {
  labId: string
  title: string
  goal: string
  tasks: StudentTaskView[]
}