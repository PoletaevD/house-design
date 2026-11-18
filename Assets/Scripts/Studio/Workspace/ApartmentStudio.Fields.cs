using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    public sealed partial class ApartmentStudio
    {
        private sealed class FieldValidation
        {
            internal Func<string, string> Error;
            internal string LastValid;
            internal string Hint;
            internal Label Message;
        }

        private TextField Number(VisualElement parent, string label, float meters, float minimum, float maximum)
        {
            var field = new TextField(label)
            {
                maxLength = 10
            };
            field.AddToClassList("studio-field");
            field.SetValueWithoutNotify(Mathf.RoundToInt(meters * 1000f).ToString(CultureInfo.InvariantCulture));
            var group = Element(parent, "field-group");
            group.Add(field);
            BindNumber(group, field, Mathf.RoundToInt(minimum * 1000f), Mathf.RoundToInt(maximum * 1000f), true, " мм");

            return field;
        }

        private void BindNumber(VisualElement group, TextField field, float minimum, float maximum, bool whole, string unit = "")
        {
            var range = $"От {FormatNumber(minimum)} до {FormatNumber(maximum)}{unit}.";
            BindValidation(group, field, validateNumber, range);

            string validateNumber(string value)
            {
                if (!NumberEditAllowed(value, minimum < 0f, !whole) || !TryNumber(value, out var parsed))
                {
                    return whole ? $"Введите целое число{unit}." : "Введите число.";
                }

                return parsed >= minimum && parsed <= maximum ? null : range;
            }
        }

        private void BindValidation(VisualElement group, TextField field, Func<string, string> error, string hint = null, Predicate<string> allowEdit = null)
        {
            var validation = new FieldValidation
            {
                Error = error,
                LastValid = field.value,
                Hint = hint,
                Message = Text(group, "", "field-error")
            };
            field.userData = validation;
            refreshField();
            field.RegisterValueChangedCallback(onValueChanged);
            field.RegisterCallback<FocusInEvent>(_ => refreshField());
            field.RegisterCallback<FocusOutEvent>(onFocusOut);

            void refreshField()
            {
                var issue = validation.Error(field.value);

                if (issue == null)
                {
                    validation.LastValid = field.value;
                }

                SetFieldError(field, validation, issue, issue);
            }

            void onValueChanged(ChangeEvent<string> evt)
            {
                if (allowEdit != null && !allowEdit(evt.newValue))
                {
                    field.SetValueWithoutNotify(evt.previousValue);
                }

                refreshField();
            }

            void onFocusOut(FocusOutEvent evt)
            {
                if (evt.relatedTarget is VisualElement target && field.Contains(target))
                {
                    return;
                }

                var issue = validation.Error(field.value);

                if (issue != null)
                {
                    if (string.IsNullOrWhiteSpace(field.value) || field.value is "-" or "." or "," or "-." or "-,")
                    {
                        field.SetValueWithoutNotify(validation.LastValid);
                    }

                    Status(issue, true);
                }

                SetFieldError(field, validation, validation.Error(field.value), issue);
            }
        }

        private void SetFieldError(TextField field, FieldValidation validation, string issue, string message)
        {
            field.EnableInClassList("invalid-input", issue != null);
            field.tooltip = issue ?? validation.Hint;
            ShowFieldMessage(validation.Message, message);
        }

        private void ShowFieldMessage(Label label, string issue)
        {
            label.text = issue ?? "";
            label.EnableInClassList("visible-error", issue != null);
        }

        private Label FieldGroupError(VisualElement parent, params TextField[] fields)
        {
            var message = Text(parent, "", "field-error");

            foreach (var field in fields)
            {
                field?.RegisterValueChangedCallback(_ => ShowFieldMessage(message, null));
            }

            return message;
        }

        private void SetNumericText(TextField field, float value)
        {
            var text = FormatNumber(value);
            field.SetValueWithoutNotify(text);

            if (field.userData is FieldValidation validation)
            {
                validation.LastValid = text;
                SetFieldError(field, validation, null, null);
            }
        }

        private void RequireFields(Button button, params TextField[] fields)
        {
            foreach (var field in fields)
            {
                if (field != null)
                {
                    field.RegisterValueChangedCallback(_ => updateAvailability());
                    field.RegisterCallback<FocusOutEvent>(_ => updateAvailability());
                }
            }

            updateAvailability();

            void updateAvailability()
            {
                foreach (var field in fields)
                {
                    if (!ValidField(field))
                    {
                        button.SetEnabled(false);
                        return;
                    }
                }

                button.SetEnabled(true);
            }
        }

        private bool ValidField(TextField field)
        {
            return field == null || field.userData is FieldValidation validation && validation.Error(field.value) == null;
        }

        private bool TryMeters(TextField field, out float meters)
        {
            meters = 0f;

            if (!ValidField(field) || !TryNumber(field.value, out var millimeters))
            {
                return false;
            }

            meters = millimeters / 1000f;

            return true;
        }

        private bool TryNumber(string value, out float number)
        {
            return float.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) && float.IsFinite(number);
        }

        private bool NumberEditAllowed(string value, bool allowNegative, bool allowDecimal)
        {
            var decimalSeen = false;

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];

                if (character is >= '0' and <= '9' || character == '-' && index == 0 && allowNegative)
                {
                    continue;
                }

                if (!allowDecimal || decimalSeen || character is not ('.' or ','))
                {
                    return false;
                }

                decimalSeen = true;
            }

            return true;
        }

        private string FormatNumber(float number)
        {
            return number.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
