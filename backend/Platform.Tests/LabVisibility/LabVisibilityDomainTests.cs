using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using Platform.LabVisibility.Application.StudentView;
using Platform.LabVisibility.Domain;

namespace Platform.Tests.LabVisibility;

public class LabVisibilityDomainTests
{
    private static LabPublicContent CreateLab(
        string title = "Test laboratorija",
        string goal = "Test cilj",
        IReadOnlyList<PublicTask>? tasks = null)
    {
        return LabPublicContent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            LabVisibilityState.Published,
            title,
            goal,
            tasks ?? new[]
            {
                new PublicTask("T1", 1, "Prvo pitanje", new[] { "A", "B" }),
                new PublicTask("T2", 2, "Drugo pitanje", Array.Empty<string>())
            });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateLab_EmptyTitle_IsRejected(string title)
    {
        Assert.Throws<ArgumentException>(() => CreateLab(title: title));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateLab_EmptyGoal_IsRejected(string goal)
    {
        Assert.Throws<ArgumentException>(() => CreateLab(goal: goal));
    }

    [Test]
    public void CreateLab_DuplicateTaskId_IsRejected()
    {
        var tasks = new[]
        {
            new PublicTask("T1", 1, "Prvo pitanje", Array.Empty<string>()),
            new PublicTask("T1", 2, "Drugo pitanje", Array.Empty<string>())
        };

        Assert.Throws<ArgumentException>(() => CreateLab(tasks: tasks));
    }

    [Test]
    public void CreateLab_TasksHaveDeterministicOrder()
    {
        var tasks = new[]
        {
            new PublicTask("T3", 2, "Treće pitanje", Array.Empty<string>()),
            new PublicTask("T2", 1, "Drugo pitanje", Array.Empty<string>()),
            new PublicTask("T1", 1, "Prvo pitanje", Array.Empty<string>())
        };

        var first = CreateLab(tasks: tasks);
        var second = CreateLab(tasks: tasks.Reverse().ToArray());

        Assert.That(
            first.Tasks.Select(task => task.TaskId),
            Is.EqualTo(new[] { "T1", "T2", "T3" }));

        Assert.That(
            second.Tasks.Select(task => task.TaskId),
            Is.EqualTo(first.Tasks.Select(task => task.TaskId)));
    }

    [Test]
    public void CreateLab_ChangingInputLists_DoesNotChangeContent()
    {
        var options = new List<string> { "A", "B" };
        var tasks = new List<PublicTask>
        {
            new PublicTask("T1", 1, "Pitanje", options)
        };

        var content = CreateLab(tasks: tasks);

        options[0] = "Izmenjeno";
        tasks.Clear();

        Assert.That(content.Tasks, Has.Count.EqualTo(1));
        Assert.That(content.Tasks[0].Options, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void CreateKey_CompleteKey_IsAcceptedAndOrdered()
    {
        var content = CreateLab();

        var key = EvaluatorKey.Create(content, new[]
        {
            new TaskAnswerKey("T2", "Drugi odgovor"),
            new TaskAnswerKey("T1", "Prvi odgovor")
        });

        Assert.That(key.LabId, Is.EqualTo(content.LabId));
        Assert.That(
            key.Answers.Select(answer => answer.TaskId),
            Is.EqualTo(new[] { "T1", "T2" }));
        Assert.That(key.Answers[0].ExpectedAnswer, Is.EqualTo("Prvi odgovor"));
        Assert.That(key.Answers[1].ExpectedAnswer, Is.EqualTo("Drugi odgovor"));
    }

    [Test]
    public void CreateKey_IncompleteKey_IsRejected()
    {
        var content = CreateLab();

        Assert.Throws<ArgumentException>(() =>
            EvaluatorKey.Create(content, new[]
            {
                new TaskAnswerKey("T1", "Odgovor")
            }));
    }

    [Test]
    public void CreateKey_UnknownTaskId_IsRejected()
    {
        var content = CreateLab();

        Assert.Throws<ArgumentException>(() =>
            EvaluatorKey.Create(content, new[]
            {
                new TaskAnswerKey("T1", "Prvi odgovor"),
                new TaskAnswerKey("T3", "Nepoznat zadatak")
            }));
    }

    [Test]
    public void CreateKey_DuplicateAnswer_IsRejected()
    {
        var content = CreateLab();

        Assert.Throws<ArgumentException>(() =>
            EvaluatorKey.Create(content, new[]
            {
                new TaskAnswerKey("T1", "Prvi odgovor"),
                new TaskAnswerKey("T1", "Duplikat"),
                new TaskAnswerKey("T2", "Drugi odgovor")
            }));
    }

    [Test]
    public void CreateKey_ChangingInputList_DoesNotChangeKey()
    {
        var answers = new List<TaskAnswerKey>
        {
            new TaskAnswerKey("T1", "Prvi odgovor"),
            new TaskAnswerKey("T2", "Drugi odgovor")
        };

        var key = EvaluatorKey.Create(CreateLab(), answers);

        answers.Clear();

        Assert.That(key.Answers, Has.Count.EqualTo(2));
    }

    [Test]
    public void Answer_AtLengthLimit_IsAccepted()
    {
        var value = new string('x', TaskAnswerKey.MaxExpectedAnswerLength);
        var answer = new TaskAnswerKey("T1", value);

        Assert.That(answer.ExpectedAnswer, Is.EqualTo(value));
    }

    [Test]
    public void Answer_OverLengthLimit_IsRejectedWithoutEchoingValue()
    {
        const string marker = "secret-marker";
        var value = marker.PadRight(TaskAnswerKey.MaxExpectedAnswerLength + 1, 'x');

        var error = Assert.Throws<ArgumentException>(() => new TaskAnswerKey("T1", value));

        Assert.That(error!.Message, Does.Not.Contain(marker));
    }

    [Test]
    public void ToString_DoesNotExposeAnswers()
    {
        const string secret = "SECRET-ANSWER-4729";
        var answer = new TaskAnswerKey("T1", secret);
        var key = EvaluatorKey.Create(CreateLab(), new[]
        {
            answer,
            new TaskAnswerKey("T2", "Drugi tajni odgovor")
        });

        Assert.That(answer.ToString(), Does.Contain("[REDACTED]"));
        Assert.That(answer.ToString(), Does.Not.Contain(secret));
        Assert.That(key.ToString(), Does.Contain("[REDACTED]"));
        Assert.That(key.ToString(), Does.Not.Contain(secret));
        Assert.That(key.ToString(), Does.Not.Contain("Drugi tajni odgovor"));
    }

    [Test]
    public void Project_CopiesAllPublicContent()
    {
        var content = CreateLab();

        var view = StudentViewProjector.Project(content);

        Assert.That(view.LabId, Is.EqualTo(content.LabId));
        Assert.That(view.Title, Is.EqualTo(content.Title));
        Assert.That(view.Goal, Is.EqualTo(content.Goal));
        Assert.That(view.Tasks.Count, Is.EqualTo(content.Tasks.Count));

        for (var i = 0; i < content.Tasks.Count; i++)
        {
            Assert.That(view.Tasks[i].TaskId, Is.EqualTo(content.Tasks[i].TaskId));
            Assert.That(view.Tasks[i].Order, Is.EqualTo(content.Tasks[i].Order));
            Assert.That(view.Tasks[i].Prompt, Is.EqualTo(content.Tasks[i].Prompt));
            Assert.That(view.Tasks[i].Options, Is.EqualTo(content.Tasks[i].Options));
        }
    }

    [Test]
    public void StudentView_Reflection_ContainsOnlyApprovedPublicProperties()
    {
        var labProperties = typeof(StudentLabView)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        var taskProperties = typeof(StudentTaskView)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        Assert.That(labProperties, Is.EquivalentTo(new[]
        {
            "LabId", "Title", "Goal", "Tasks"
        }));

        Assert.That(taskProperties, Is.EquivalentTo(new[]
        {
            "TaskId", "Order", "Prompt", "Options"
        }));

        // Provera obuhvata i privatna polja, uključujući backing fields.
        foreach (var type in new[] { typeof(StudentLabView), typeof(StudentTaskView) })
        {
            var fields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                Assert.That(
                    field.Name.Contains("answer", StringComparison.OrdinalIgnoreCase),
                    Is.False, $"Nedozvoljeno polje: {field.Name}");

                Assert.That(
                    field.Name.Contains("key", StringComparison.OrdinalIgnoreCase),
                    Is.False, $"Nedozvoljeno polje: {field.Name}");

                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(EvaluatorKey)));
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(TaskAnswerKey)));
            }
        }
    }

    [Test]
    public void StudentView_JsonContainsOnlyPublicData()
    {
        var view = StudentViewProjector.Project(CreateLab());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view));

        Assert.That(
            json.RootElement.EnumerateObject().Select(property => property.Name),
            Is.EquivalentTo(new[] { "LabId", "Title", "Goal", "Tasks" }));

        foreach (var task in json.RootElement.GetProperty("Tasks").EnumerateArray())
        {
            Assert.That(
                task.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "TaskId", "Order", "Prompt", "Options" }));
        }
    }

    [Test]
    public void Project_NullContent_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            StudentViewProjector.Project(null!));
    }
}