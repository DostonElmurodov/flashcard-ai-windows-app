import XCTest

final class UILaunchFixtureTests: XCTestCase {
    func testLearnSearchOpensARealEditableField() {
        let app = XCUIApplication()
        app.launchArguments = ["-owl-ui-fixture", "empty-library"]
        app.launch()

        let search = app.buttons["Search Learn"]
        XCTAssertTrue(search.waitForExistence(timeout: 10), app.debugDescription)
        search.tap()

        let field = app.textFields["Learn search field"]
        XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
        XCTAssertTrue(app.buttons["Close Learn search"].exists)
    }
}
