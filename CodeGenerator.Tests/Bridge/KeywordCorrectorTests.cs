using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class KeywordCorrectorTests
{
    [TestMethod]
    [DataRow("busniess", "business")]
    [DataRow("bussiness", "business")]
    [DataRow("valudation", "validation")]
    [DataRow("volidation", "validation")]
    [DataRow("validater", "validator")]
    public void Corrects_near_miss_keywords(string typo, string expected)
        => Assert.AreEqual(expected, KeywordCorrector.Correct(typo));

    [TestMethod]
    [DataRow("validaton")]  // one edit from BOTH validation and validator
    [DataRow("validaiton")] // two edits from BOTH — no unique nearest keyword
    public void Leaves_ambiguous_typos_alone_rather_than_guessing(string typo)
        => Assert.AreEqual(typo, KeywordCorrector.Correct(typo));

    [TestMethod]
    [DataRow("business")]
    [DataRow("validation")]
    [DataRow("validator")]
    public void Leaves_correct_spellings_unchanged(string word)
        => Assert.AreEqual(word, KeywordCorrector.Correct(word));

    [TestMethod]
    public void Corrects_only_the_keyword_within_a_sentence()
        => Assert.AreEqual(
            "add business validation Number should not be blank in Bill",
            KeywordCorrector.Correct("add busniess validation Number should not be blank in Bill"));

    [TestMethod]
    [DataRow("Number")]
    [DataRow("Bill")]
    [DataRow("Location")]
    [DataRow("Vacation")]
    [DataRow("Customer")]
    public void Leaves_argument_and_entity_words_untouched(string word)
        => Assert.AreEqual(word, KeywordCorrector.Correct(word));

    [TestMethod]
    public void Does_not_cross_correct_validator_and_validation()
    {
        // Both are keywords already; neither should be rewritten to the other.
        Assert.AreEqual("validator", KeywordCorrector.Correct("validator"));
        Assert.AreEqual("validation", KeywordCorrector.Correct("validation"));
    }

    [TestMethod]
    public void Preserves_blank_and_null_input()
    {
        Assert.AreEqual("", KeywordCorrector.Correct(""));
        Assert.AreEqual("   ", KeywordCorrector.Correct("   "));
        Assert.AreEqual("", KeywordCorrector.Correct(null!));
    }

    // `validate` is a real word two edits from `validator`, so the distance rule
    // "corrected" it and CommandFront's \bvalidator\b guard swallowed the sentence.
    [TestMethod]
    [DataRow("validate")]
    [DataRow("validates")]
    [DataRow("validated")]
    [DataRow("valid")]
    public void Leaves_real_words_alone_even_when_they_are_near_a_keyword(string word)
        => Assert.AreEqual(word, KeywordCorrector.Correct(word));

    [TestMethod]
    public void Leaves_a_protected_word_alone_inside_a_sentence()
        => Assert.AreEqual("validate project", KeywordCorrector.Correct("validate project"));

    // Case must not be a way around the protection.
    [TestMethod]
    [DataRow("Validate")]
    [DataRow("VALIDATE")]
    public void Protection_is_case_insensitive(string word)
        => Assert.AreEqual(word, KeywordCorrector.Correct(word));
}
