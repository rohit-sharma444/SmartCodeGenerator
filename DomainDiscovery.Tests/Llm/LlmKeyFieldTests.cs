using System.Text.Json;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// Measured on the Ollama path (school, car rental and gym prompts): every class came out with
/// <c>Id</c> AND its own <c>CourseID</c>/<c>VehicleID</c>, a user-typed int defaulting to 0 that
/// nothing checked was unique; and <c>Teacher.SchoolID</c>, <c>Course.SchoolID</c>,
/// <c>Employee.GymId</c>, <c>FitnessEquipment.GymId</c>, <c>Vehicle.VehicleTypeID</c> stayed plain
/// ints although School, Gym and VehicleType were classes in the same model.
/// </summary>
public class LlmKeyFieldTests
{
    // The shape the default model answers in: its own key spelled ClassID, references spelled
    // OtherID or OtherId, and roles that are not Document/Transaction/Event.
    private const string SchoolReply = """
        { "classes": [
          { "name": "School", "role": "Place", "properties": [
              { "name": "SchoolID", "type": "int" }, { "name": "Name", "type": "string" } ] },
          { "name": "Teacher", "role": "Party", "properties": [
              { "name": "TeacherID", "type": "int" }, { "name": "FullName", "type": "string" },
              { "name": "SchoolID", "type": "int" } ] },
          { "name": "Course", "role": "Item", "properties": [
              { "name": "id", "type": "int" }, { "name": "CourseID", "type": "int" },
              { "name": "Title", "type": "string" }, { "name": "SchoolID", "type": "int" } ] },
          { "name": "VehicleType", "role": "None", "properties": [
              { "name": "VehicleTypeID", "type": "int" }, { "name": "Name", "type": "string" } ] },
          { "name": "Vehicle", "role": "Item", "properties": [
              { "name": "VehicleID", "type": "int" }, { "name": "Plate", "type": "string" },
              { "name": "VehicleTypeID", "type": "int" } ] },
          { "name": "FitnessEquipment", "role": "Device", "properties": [
              { "name": "Name", "type": "string" }, { "name": "schoolid", "type": "int" } ] },
          { "name": "Enrolment", "role": "Transaction", "properties": [
              { "name": "EnrolmentID", "type": "int" }, { "name": "CourseID", "type": "int" } ] },
          { "name": "Customer", "role": "Party", "properties": [
              { "name": "FullName", "type": "string" } ] },
          { "name": "CustomerReview", "role": "Document", "properties": [
              { "name": "ReviewID", "type": "int" }, { "name": "CustomerID", "type": "int" },
              { "name": "Rating", "type": "int" } ] },
          { "name": "RentalAgencyEmployee", "role": "Party", "properties": [
              { "name": "EmployeeID", "type": "int" }, { "name": "FullName", "type": "string" } ] },
          { "name": "SchoolCourse", "role": "Item", "properties": [
              { "name": "CourseID", "type": "int" }, { "name": "Term", "type": "string" } ] }
        ] }
        """;

    private static CandidateDomainModel Normalized()
    {
        var reply = JsonSerializer.Deserialize<LlmDomainModel>(SchoolReply)!;
        var client = new FakeOllamaClient { Answer = reply };
        var outcome = new OllamaDomainModelSource(client, "qwen2.5:3b", phases: 1).Discover("a school");
        Assert.True(outcome.Succeeded);
        return outcome.Model;
    }

    private static CandidateEntity Class(string name) => Normalized().Entities.Single(e => e.Name == name);

    [Theory]
    [InlineData("School", "SchoolID")]
    [InlineData("Teacher", "TeacherID")]
    [InlineData("Course", "CourseID")]
    [InlineData("Vehicle", "VehicleID")]
    [InlineData("Enrolment", "EnrolmentID")]
    // Measured live (car rental): the model keys a compound class by its LAST word -
    // CustomerReview.ReviewID, RentalAgencyEmployee.EmployeeID - and those survived the first fix.
    [InlineData("CustomerReview", "ReviewID")]
    [InlineData("RentalAgencyEmployee", "EmployeeID")]
    public void A_field_that_repeats_the_classs_own_key_is_dropped(string className, string ownKey)
    {
        var entity = Class(className);

        Assert.DoesNotContain(entity.Fields, f => f.Name.Equals(ownKey, StringComparison.OrdinalIgnoreCase));
        Assert.Single(entity.Fields, f => f.Name == "Id");
    }

    [Theory]
    [InlineData("Teacher", "School")]
    [InlineData("Course", "School")]
    [InlineData("Vehicle", "VehicleType")]
    [InlineData("FitnessEquipment", "School")]
    [InlineData("Enrolment", "Course")]
    [InlineData("CustomerReview", "Customer")]
    // A last word that is itself a class is a reference to it, not the own key.
    [InlineData("SchoolCourse", "Course")]
    public void A_reference_to_a_class_in_the_model_is_a_relationship(string className, string target)
    {
        var entity = Class(className);

        Assert.Contains(entity.Relationships, r => r.Target == target);
        Assert.Single(entity.Fields, f => f.Name.Equals(target + "Id", StringComparison.OrdinalIgnoreCase));
    }
}
